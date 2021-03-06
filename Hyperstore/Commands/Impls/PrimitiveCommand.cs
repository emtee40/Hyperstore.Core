//	Copyright � 2013 - 2014, Alain Metge. All rights reserved.
//
//		This file is part of Hyperstore (http://www.hyperstore.org)
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
 
using System;
namespace Hyperstore.Modeling.Commands
{
    ///-------------------------------------------------------------------------------------------------
    /// <summary>
    ///  A primitive command.
    /// </summary>
    /// <seealso cref="T:Hyperstore.Modeling.Commands.IDomainCommand"/>
    ///-------------------------------------------------------------------------------------------------
    public abstract class PrimitiveCommand : IDomainCommand
    {
        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Initializes a new instance of the <see cref="AbstractDomainCommand" /> class.
        /// </summary>
        /// <param name="domainModel">
        ///  The domain model.
        /// </param>
        /// <param name="version">
        ///  The version.
        /// </param>
        ///-------------------------------------------------------------------------------------------------
        protected PrimitiveCommand(IDomainModel domainModel, long? version)
        {
            Contract.Requires(domainModel, "domainModel");
            DomainModel = domainModel;
            Version = version ?? DateTime.UtcNow.Ticks;
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets the domain model.
        /// </summary>
        /// <value>
        ///  The domain model.
        /// </value>
        ///-------------------------------------------------------------------------------------------------
        public IDomainModel DomainModel { get; private set; }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets or sets the version (UtcNow.Ticks)
        /// </summary>
        /// <value>
        ///  The version.
        /// </value>
        ///-------------------------------------------------------------------------------------------------
        public long? Version { get; private set; }
    }
}