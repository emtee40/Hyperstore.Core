﻿//	Copyright � 2013 - 2014, Alain Metge. All rights reserved.
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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Hyperstore.Modeling;
using Hyperstore.Modeling.Commands;
using Hyperstore.Tests.Model;
using Hyperstore.Modeling.Metadata.Constraints;

namespace Hyperstore.Tests.Model
{
    public partial class Library : ICheckConstraint<Library>
    {
        void ICheckConstraint<Library>.ExecuteConstraint(Library mel, ConstraintContext ctx)
        {
         
        }
    }

    public class EmailSchema : Hyperstore.Modeling.Metadata.SchemaValueObject<string>, ICheckValueObjectConstraint<string>
    {
        private ISchemaInfo _underlyingSchema;
        private object _defautValue;
        private bool _defaultInitialized;

        public EmailSchema(ISchema schema)
            : base(schema)
        {            
        }

        protected override void Initialize(ISchemaElement schemaElement, IDomainModel domainModel)
        {
            base.Initialize(schemaElement, domainModel);
            _underlyingSchema = domainModel.Store.GetSchemaInfo<string>();
        }

        protected override object Deserialize(SerializationContext ctx)
        {
            return _underlyingSchema.Deserialize( ctx );
        }

        protected override object Serialize(object data)
        {
            return _underlyingSchema.Serialize(data);
        }

        protected override object DefaultValue
        {
            get
            {
                if (!_defaultInitialized)
                {
                    _defautValue = _underlyingSchema.DefaultValue;
                    _defaultInitialized = true;
                }
                return _defautValue;
            }
            set
            {
                _defaultInitialized = true;
                _defautValue = value;
            }
        }

        public void ExecuteConstraint(string value, string oldValue, ConstraintContext ctx)
        {
            if (value == null)
                return;
            try
            {
                new System.Net.Mail.MailAddress(value);
            }
            catch(Exception)
            {
                ctx.CreateErrorMessage("Invalid email address {Email} for {Name}");
            }
        }
    }
}

namespace Hyperstore.Tests
{
    /// <summary>
    /// Creation d'une classe X dont le name est test
    /// </summary>
    class MyCommand : AbstractDomainCommand, ICommandHandler<MyCommand>
    {
        public XExtendsBaseClass Element { get; private set; }

        public MyCommand(IDomainModel domainModel)
            : base(domainModel)
        {
        }

        public Modeling.Events.IEvent Handle(ExecutionCommandContext<MyCommand> context)
        {
            Element = new XExtendsBaseClass(DomainModel);
            Element.Name = "Test";
            return new MyEvent(DomainModel, context.CurrentSession.SessionId);
        }
    }

    public class MyEvent : Hyperstore.Modeling.Events.AbstractDomainEvent
    {
        public MyEvent(IDomainModel domainModel, int correlationId)
            : base(domainModel.Name, domainModel.ExtensionName, 1, correlationId)
        {

        }
    }
}
