#!/usr/bin/env -S dotnet fsi

#I "../tests/DiffSharp.Tests/bin/Debug/net5.0"
#r "DiffSharp.Core.dll"
#r "DiffSharp.Data.dll"
#r "DiffSharp.Backends.Torch.dll"
// #r "nuget: libtorch-cuda-10.2-linux-x64, 1.7.0.1"
System.Runtime.InteropServices.NativeLibrary.Load("/home/gunes/anaconda3/lib/python3.8/site-packages/torch/lib/libtorch.so")


open DiffSharp
open DiffSharp.Compose
open DiffSharp.Model
open DiffSharp.Data
open DiffSharp.Optim
open DiffSharp.Util

dsharp.config(backend=Backend.Torch, device=Device.CPU)
dsharp.seed(1)

let rnnShape (value:Tensor) inFeatures batchFirst =
    let value =
        if batchFirst then
            if value.dim <> 3 then failwithf "Expecting the input to be of shape batchSize x seqLen x inFeatures, but received input with shape %A" value.shape
            value.transpose(0, 1)
        else
            if value.dim <> 3 then failwithf "Expecting the input to be of shape seqLen x batchSize x inFeatures, but received input with shape %A" value.shape
            value
    if value.shape.[2] <> inFeatures then failwithf "Expecting input to have %A features, but received input with shape %A" inFeatures value.shape
    let seqLen, batchSize = value.shape.[0], value.shape.[1]
    value, seqLen, batchSize


type RNNCell(inFeatures, outFeatures, ?nonlinearity, ?bias, ?batchFirst) =
    inherit Model()
    let nonlinearity = defaultArg nonlinearity dsharp.tanh
    let bias = defaultArg bias true
    let batchFirst = defaultArg batchFirst false
    let k = 1./sqrt (float outFeatures)
    let wih = Parameter(Weight.uniform([|inFeatures; outFeatures|], k))
    let whh = Parameter(Weight.uniform([|outFeatures; outFeatures|], k))
    let b = Parameter(if bias then Weight.uniform([|outFeatures|], k) else dsharp.tensor([]))
    let h = Parameter <| dsharp.tensor([])
    do base.add([wih;whh;b],["RNNCell-weight-ih";"RNNCell-weight-hh";"RNNCell-bias"])

    member _.hidden 
        with get () = h.value
        and set v = h.value <- v

    override _.getString() = sprintf "RNNCell(%A, %A)" inFeatures outFeatures

    member r.reset() = r.hidden <- dsharp.tensor([])

    override r.forward(value) =
        let value, seqLen, batchSize = rnnShape value inFeatures batchFirst
        if r.hidden.nelement = 0 then r.hidden <- dsharp.zeros([batchSize; outFeatures])
        let output = Array.create seqLen (dsharp.tensor([]))
        for i in 0..seqLen-1 do
            let v = value.[i]
            r.hidden <- dsharp.matmul(v, wih.value) + dsharp.matmul(h.value, whh.value)
            if bias then r.hidden <- r.hidden + b.value
            r.hidden <- nonlinearity r.hidden
            output.[i] <- r.hidden
        let output = dsharp.stack output
        if batchFirst then output.transpose(0, 1) else output


type RNN(inFeatures, outFeatures, ?numLayers, ?nonlinearity, ?bias, ?batchFirst, ?dropout, ?bidirectional) =
    inherit Model()
    let numLayers = defaultArg numLayers 1
    let dropout = defaultArg dropout 0.
    let bidirectional = defaultArg bidirectional false
    let batchFirst = defaultArg batchFirst false
    let numDirections = if bidirectional then 2 else 1
    let makeLayers () = Array.init numLayers (fun i -> if i = 0 then RNNCell(inFeatures, outFeatures, ?nonlinearity=nonlinearity, ?bias=bias) else RNNCell(outFeatures, outFeatures, ?nonlinearity=nonlinearity, ?bias=bias))
    let layers = makeLayers()
    let layersReverse = if bidirectional then makeLayers() else [||]
    let dropoutLayer = Dropout(dropout)
    let hs = Parameter <| dsharp.tensor([])
    do 
        base.add(layers |> Array.map box, Array.init numLayers (fun i -> sprintf "RNN-layer-%A" i))
        if bidirectional then base.add(layersReverse |> Array.map box, Array.init numLayers (fun i -> sprintf "RNN-layer-reverse-%A" i))
        if dropout > 0. then base.add([dropoutLayer], ["RNN-dropout"])

    member _.hidden
        with get () = hs.value
        and set v = hs.value <- v

    override _.getString() = sprintf "RNN(%A, %A, numLayers:%A)" inFeatures outFeatures numLayers

    member r.reset() = r.hidden <- dsharp.tensor([])

    override r.forward(value) =
        let value, _, batchSize = rnnShape value inFeatures batchFirst
        if r.hidden.nelement = 0 then r.hidden <- dsharp.zeros([numLayers*numDirections; batchSize; outFeatures])
        let newhs = Array.create (numLayers*numDirections) (dsharp.tensor([]))
        let mutable hFwd = value
        for i in 0..numLayers-1 do 
            layers.[i].hidden <- r.hidden.[i]
            hFwd <- layers.[i].forward(hFwd)
            if dropout > 0. && i < numLayers-1 then hFwd <- dropoutLayer.forward(hFwd)
            newhs.[i] <- layers.[i].hidden
        let output = 
            if bidirectional then
                let mutable hRev = value.flip([0])
                for i in 0..numLayers-1 do 
                    layersReverse.[i].hidden <- r.hidden.[numLayers+i]
                    hRev <- layersReverse.[i].forward(hRev)
                    if dropout > 0. && i < numLayers-1 then hRev <- dropoutLayer.forward(hRev)
                    newhs.[numLayers+i] <- layersReverse.[i].hidden
                dsharp.cat([hFwd; hRev], 2)
            else hFwd
        r.hidden <- dsharp.stack(newhs)
        if batchFirst then output.transpose(0, 1) else output


// let rnn = RNN(32, 10, bidirectional=true, numLayers=2, bias=false, dropout=0.3)
// let x = dsharp.randn([4; 16; 32])
// print rnn.hidden.shape
// let h = x --> rnn
// print rnn.hidden.shape
// print h.shape 
// rnn.reset()

let text = "A merry little surge of electricity piped by automatic alarm from the mood organ beside his bed awakened Rick Deckard."

type TextTokenizer(?sampleText) = 
    let sampleText = defaultArg sampleText """0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ!"#$%&\'()*+,-./:;?@[\\]^_`{|}~ """
    let chars = sampleText |> Seq.distinct |> Seq.toArray
    member _.length = chars.Length
    member _.charToIndex(c) =
        let i = 
            try
                Array.findIndex ((=) c) chars
            with
            | _ -> failwithf "Given char '%A' is not a part of this tokenizer %A" c chars
        i
    member t.textToIndices(text) = text |> Seq.map t.charToIndex |> Seq.toArray
    member t.indicesToTensor(indices) = indices |> Array.map (fun i -> dsharp.onehot(t.length, i)) |> dsharp.stack
    member t.textToTensor(text) = t.textToIndices(text) |> t.indicesToTensor
    member t.indexToChar(index) = chars.[index]
    member t.tensorToText(tensor:Tensor) =
        if tensor.dim <> 2 then failwithf "Expecting a 2d tensor with shape seqLen x features, received tensor with shape %A" tensor.shape
        [|for i in 0..tensor.shape.[0]-1 do tensor.[i].argmax().[0]|] |> Array.map t.indexToChar |> System.String
    member t.dataset(text:string, seqLength) =
        if seqLength > text.Length then failwithf "Expecting text.Length (%A) >= seqLength (%A)" text.Length seqLength
        let sequences = [|for i in 0..(text.Length - seqLength + 1)-1 do text.Substring(i, seqLength)|] |> Array.map t.textToIndices
        let data = sequences |> Array.map t.indicesToTensor |> dsharp.stack
        let target = sequences |> Array.map dsharp.tensor |> dsharp.stack
        TensorDataset(data, target)


let seqLen = 32
let tok = TextTokenizer()
let dataset = tok.dataset(text, seqLen)
let loader = dataset.loader(batchSize=4)

let rnn = RNN(tok.length, tok.length, numLayers=3, batchFirst=true)
print rnn
let optimizer = Adam(rnn, lr=dsharp.tensor(0.0005))

let epochs = 15
let start = System.DateTime.Now
for epoch = 1 to epochs do
    for i, x, t in loader.epoch() do
        let input =  x.[*,..seqLen-2]
        let target = t.[*,1..]
        // printfn "input  %A" input.shape
        // printfn "target %A" target.shape
        rnn.reset()
        rnn.reverseDiff()
        let output = input --> rnn
        // printfn "output %A" output.shape
        // printfn ""
        let loss = dsharp.crossEntropyLoss(output.transpose(1, 2), target)
        loss.reverse()
        optimizer.step()
        print loss


// let mutable c = ["e"]
// for i in 0..5 do
//     let cc = c |> List.last |> tok.textToTensor |> dsharp.unsqueeze(1) |> rnn.forward |> dsharp.squeeze(1) |> tok.tensorToText
//     c <- c@[cc]

// print c