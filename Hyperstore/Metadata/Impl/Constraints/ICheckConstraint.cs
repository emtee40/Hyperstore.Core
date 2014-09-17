﻿// Copyright 2014 Zenasoft.  All rights reserved.
//
// This file is part of Hyperstore.
//
//    Hyperstore is free software: you can redistribute it and/or modify
//    it under the terms of the GNU General Public License as published by
//    the Free Software Foundation, either version 3 of the License, or
//    (at your option) any later version.
//
//    Hyperstore is distributed in the hope that it will be useful,
//    but WITHOUT ANY WARRANTY; without even the implied warranty of
//    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//    GNU General Public License for more details.
//
//    You should have received a copy of the GNU General Public License
//    along with Hyperstore.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hyperstore.Modeling.Metadata.Constraints
{
    internal interface ICheckConstraint
    {
        void ExecuteConstraint(IModelElement mel, ConstraintContext ctx);
    }

    public interface ICheckConstraint<T> where T : IModelElement
    {
        void ExecuteConstraint(T mel, ConstraintContext ctx);
    }

    public interface IValidationConstraint<T> : ICheckConstraint<T> where T : IModelElement
    {
        string Category { get; }
    }
}