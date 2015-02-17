﻿//
// This file is part of
// DiffSharp -- F# Automatic Differentiation Library
//
// Copyright (C) 2014, 2015, National University of Ireland Maynooth.
//
//   DiffSharp is free software: you can redistribute it and/or modify
//   it under the terms of the GNU General Public License as published by
//   the Free Software Foundation, either version 3 of the License, or
//   (at your option) any later version.
//
//   DiffSharp is distributed in the hope that it will be useful,
//   but WITHOUT ANY WARRANTY; without even the implied warranty of
//   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//   GNU General Public License for more details.
//
//   You should have received a copy of the GNU General Public License
//   along with DiffSharp. If not, see <http://www.gnu.org/licenses/>.
//
// Written by:
//
//   Atilim Gunes Baydin
//   atilimgunes.baydin@nuim.ie
//
//   Barak A. Pearlmutter
//   barak@cs.nuim.ie
//
//   Hamilton Institute & Department of Computer Science
//   National University of Ireland Maynooth
//   Maynooth, Co. Kildare
//   Ireland
//
//   www.bcl.hamilton.ie
//

module DiffSharp.Tests

open Xunit
open FsCheck
open FsCheck.Xunit


let ``is valid float`` x =
    not (System.Double.IsInfinity(x) || System.Double.IsNegativeInfinity(x) || System.Double.IsPositiveInfinity(x) || System.Double.IsNaN(x) || (x = System.Double.MinValue) || (x = System.Double.MaxValue))

let (=~) x y =
    abs ((x - y) / x) < 1e-6


[<Property(Verbose = true)>]
let ``AD.Forward diff = Symbolic diff`` (x:float) =
    (``is valid float`` x) ==> ((DiffSharp.AD.Forward.ForwardOps.diff (fun x -> (sin x) * (cos (exp x))) x) =~ (DiffSharp.Symbolic.SymbolicOps.diff <@ fun x -> (sin x) * (cos (exp x)) @> x))

[<Property(Verbose = true)>]
let ``AD.Forward2 diff = Symbolic diff`` (x:float) =
    (``is valid float`` x) ==> ((DiffSharp.AD.Forward2.Forward2Ops.diff (fun x -> (sin x) * (cos (exp x))) x) =~ (DiffSharp.Symbolic.SymbolicOps.diff <@ fun x -> (sin x) * (cos (exp x)) @> x))

[<Property(Verbose = true)>]
let ``AD.ForwardN diff = Symbolic diff`` (x:float) =
    (``is valid float`` x) ==> ((DiffSharp.AD.ForwardN.ForwardNOps.diff (fun x -> (sin x) * (cos (exp x))) x) =~ (DiffSharp.Symbolic.SymbolicOps.diff <@ fun x -> (sin x) * (cos (exp x)) @> x))

[<Property(Verbose = true)>]
let ``AD.ForwardG diff = Symbolic diff`` (x:float) =
    (``is valid float`` x) ==> ((DiffSharp.AD.ForwardG.ForwardGOps.diff (fun x -> (sin x) * (cos (exp x))) x) =~ (DiffSharp.Symbolic.SymbolicOps.diff <@ fun x -> (sin x) * (cos (exp x)) @> x))

[<Property(Verbose = true)>]
let ``AD.ForwardGH diff = Symbolic diff`` (x:float) =
    (``is valid float`` x) ==> ((DiffSharp.AD.ForwardGH.ForwardGHOps.diff (fun x -> (sin x) * (cos (exp x))) x) =~ (DiffSharp.Symbolic.SymbolicOps.diff <@ fun x -> (sin x) * (cos (exp x)) @> x))


