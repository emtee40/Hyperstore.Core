﻿//	Copyright © 2013 - 2014, Alain Metge. All rights reserved.
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
 
#region Imports

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Hyperstore.Modeling.Commands;
using Hyperstore.Modeling.Metadata.Primitives;
using Hyperstore.Modeling.Utils;
using Hyperstore.Modeling.Platform;
using Hyperstore.Modeling.Metadata.Constraints;

#endregion

namespace Hyperstore.Modeling.Metadata
{
    ///-------------------------------------------------------------------------------------------------
    /// <summary>
    ///  Information about the schema.
    /// </summary>
    /// <seealso cref="T:Hyperstore.Modeling.ModelElement"/>
    /// <seealso cref="T:Hyperstore.Modeling.ISchemaInfo"/>
    ///-------------------------------------------------------------------------------------------------
    [DebuggerDisplay("SchemaElement {DebuggerDisplay,nq}")]
    public abstract class SchemaInfo : ModelElement, ISchemaInfo
    {
        #region Enums of MetaClass (7)

        private Type _implementedType;
        private string _name;
        private ModelElementCollection<ISchemaProperty> _properties;
        private IConcurrentDictionary<string, ISchemaProperty> _propertiesByName;
        private bool _propertiesLoaded;
        private ISchemaElement _superClass;
        private ReferenceHandler _superClassHandler;

        #endregion Enums of MetaClass (7)

        #region Properties of MetaClass (6)

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets the default super class.
        /// </summary>
        /// <value>
        ///  The default super class.
        /// </value>
        ///-------------------------------------------------------------------------------------------------
        protected virtual ISchemaElement DefaultSuperClass
        {
            get { return Schema.Store.PrimitivesSchema.ModelEntitySchema; }
        }


        object ISchemaInfo.DefaultValue
        {
            get { return DefaultValue; }
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets or sets the default value.
        /// </summary>
        /// <exception cref="NotImplementedException">
        ///  Thrown when the requested operation is unimplemented.
        /// </exception>
        /// <value>
        ///  The default value.
        /// </value>
        ///-------------------------------------------------------------------------------------------------
        protected virtual object DefaultValue
        {
            get
            {
                if (ReflectionHelper.IsValueType(ImplementedType) && Nullable.GetUnderlyingType(ImplementedType) == null)
                    return Activator.CreateInstance(ImplementedType);
                return null;
            }
            set { throw new NotImplementedException(); }
        }

        /// <summary>
        ///     Gets the implemented type.
        /// </summary>
        Type ISchemaInfo.ImplementedType
        {
            get { return ImplementedType; }
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets or sets the implemented type.
        /// </summary>
        /// <value>
        ///  The type of the implemented.
        /// </value>
        ///-------------------------------------------------------------------------------------------------
        protected Type ImplementedType
        {
            get { return _implementedType ?? (_implementedType = GetPropertyValue<Type>("ImplementedType")); }
            set { SetPropertyValue("ImplementedType", _implementedType = value); }
        }

        bool ISchemaInfo.IsPrimitive
        {
            get { return IsPrimitive; }
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets a value indicating whether this instance is primitive.
        /// </summary>
        /// <value>
        ///  true if this instance is primitive, false if not.
        /// </value>
        ///-------------------------------------------------------------------------------------------------
        protected bool IsPrimitive
        {
            get { return false; }
        }

        string ISchemaInfo.Name
        {
            get { return Name; }
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets or sets the name.
        /// </summary>
        /// <value>
        ///  The name.
        /// </value>
        ///-------------------------------------------------------------------------------------------------
        protected string Name
        {
            get { return _name ?? (_name = GetPropertyValue<string>("Name")); }
            set { SetPropertyValue("Name", _name = value); }
        }

        ISchemaElement ISchemaInfo.SuperClass
        {
            get { return SuperClass; }
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets or sets the super class.
        /// </summary>
        /// <value>
        ///  The super class.
        /// </value>
        ///-------------------------------------------------------------------------------------------------
        protected ISchemaElement SuperClass
        {
            get { return _superClass ?? (_superClass = (_superClassHandler.GetReference<ISchemaElement>() ?? DefaultSuperClass)); }
            set { _superClassHandler.SetReference(_superClass = value); }
        }

        #endregion Properties of MetaClass (6)

        #region Constructors of MetaClass (2)

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Specialized default constructor for use only by derived classes.
        /// </summary>
        ///-------------------------------------------------------------------------------------------------
        protected SchemaInfo()
        {
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Initializes a new instance of the <see cref="SchemaEntity" /> class.
        /// </summary>
        /// <param name="schema">
        ///  The domain model.
        /// </param>
        /// <param name="implementedType">
        ///  Type of the implemented.
        /// </param>
        /// <param name="name">
        ///  The name.
        /// </param>
        /// <param name="id">
        ///  (Optional) The id.
        /// </param>
        /// <param name="superMetaClass">
        ///  (Optional) The super meta class.
        /// </param>
        /// <param name="metaclass">
        ///  (Optional) The metaclass.
        /// </param>
        ///-------------------------------------------------------------------------------------------------
        protected SchemaInfo(ISchema schema, Type implementedType, string name, Identity id = null, ISchemaElement superMetaClass = null, ISchemaElement metaclass = null)
        {
            Contract.Requires(schema, "schema");

            if (implementedType == null)
                implementedType = typeof(DynamicModelEntity);

            name = Conventions.ExtractMetaElementName(schema.Name, name ?? implementedType.FullName);

            if (id == null)
                id = new Identity(schema.Name, name);

            //if (implementedType != typeof(DynamicModelEntity))
            //{
            //    var baseType = ReflectionHelper.GetBaseType(implementedType);
            //    if (baseType != null)
            //    {
            //        var baseClass = schema.Store.GetSchemaInfo(baseType.FullName, false);
            //        if (baseClass != null)
            //        {
            //            if (superMetaClass == null || !superMetaClass.IsA(baseClass))
            //                throw new Exception("Superclass mismatch with inherited type");
            //        }
            //    }
            //}

            ConstructInternal(schema, implementedType, id, name, superMetaClass, metaclass, (dm, melId, m) => new AddSchemaEntityCommand(dm as ISchema, melId, (ISchemaEntity)m));
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Construct internal.
        /// </summary>
        /// <param name="metaModel">
        ///  The meta model.
        /// </param>
        /// <param name="implementedType">
        ///  Type of the implemented.
        /// </param>
        /// <param name="id">
        ///  The id.
        /// </param>
        /// <param name="name">
        ///  The name.
        /// </param>
        /// <param name="superMetaClass">
        ///  The super meta class.
        /// </param>
        /// <param name="metaclass">
        ///  The metaclass.
        /// </param>
        /// <param name="commandFactory">
        ///  The command factory.
        /// </param>
        ///-------------------------------------------------------------------------------------------------
        protected void ConstructInternal(IDomainModel metaModel, Type implementedType, Identity id, string name, ISchemaElement superMetaClass, ISchemaElement metaclass, Func<IDomainModel, Identity, ISchemaElement, IDomainCommand> commandFactory)
        {
            DebugContract.Requires(metaModel);
            DebugContract.Requires(metaModel is ISchema, "metaModel");

            Super(metaModel, // Pour être certain de prendre le metamodele
                    metaclass, commandFactory, id);

            ImplementedType = implementedType;
            Name = name;

            if (superMetaClass != null)
                SuperClass = superMetaClass;
        }

        #endregion Constructors of MetaClass (2)

        #region Methods of MetaClass (12)

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets the schema.
        /// </summary>
        /// <value>
        ///  The schema.
        /// </value>
        ///-------------------------------------------------------------------------------------------------
        public ISchema Schema
        {
            get
            {
                return DomainModel as ISchema;
            }
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Deserializes an element from the specified context.
        /// </summary>
        /// <exception cref="Exception">
        ///  Thrown when an exception error condition occurs.
        /// </exception>
        /// <param name="ctx">
        ///  Serialization context.
        /// </param>
        /// <returns>
        ///  An object.
        /// </returns>
        ///-------------------------------------------------------------------------------------------------
        protected virtual object Deserialize(SerializationContext ctx)
        {
            return ctx.Value;
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Serializes the specified data.
        /// </summary>
        /// <param name="data">
        ///  The data.
        /// </param>
        /// <returns>
        ///  A string.
        /// </returns>
        ///-------------------------------------------------------------------------------------------------
        protected virtual object Serialize(object data)
        {
            return data;
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets the relationships.
        /// </summary>
        /// <param name="metadata">
        ///  (Optional) the metadata.
        /// </param>
        /// <param name="end">
        ///  (Optional)
        /// </param>
        /// <returns>
        ///  An enumerator that allows foreach to be used to process the relationships in this collection.
        /// </returns>
        ///-------------------------------------------------------------------------------------------------
        public override IEnumerable<IModelRelationship> GetRelationships(ISchemaRelationship metadata = null, IModelElement end = null)
        {
            Queue<IModelElement> elements = new Queue<IModelElement>();
            elements.Enqueue(this);
            while (elements.Count > 0)
            {
                var elem = elements.Dequeue();
                foreach (var rel in DomainModel.GetRelationships(metadata, elem, end))
                {
                    yield return rel;
                }
                var s = ((ISchemaElement)elem).SuperClass;
                if (s != null)
                    elements.Enqueue(s);
            }
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Determines whether the specified meta class is A.
        /// </summary>
        /// <param name="metaClass">
        ///  The meta class.
        /// </param>
        /// <returns>
        ///  <c>true</c> if the specified meta class is A; otherwise, <c>false</c>.
        /// </returns>
        ///-------------------------------------------------------------------------------------------------
        protected virtual bool IsA(ISchemaInfo metaClass)
        {
            Contract.Requires(metaClass, "metaClass");

            if (metaClass != null && metaClass.Id == ((IModelElement)this).Id)
                return true;

            var superType = SuperClass;
            return superType != null && superType.IsA(metaClass);
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Defines the property.
        /// </summary>
        /// <param name="name">
        ///  The name.
        /// </param>
        /// <param name="metadata">
        ///  The metadata.
        /// </param>
        /// <param name="kind">
        ///  (Optional) the kind.
        /// </param>
        /// <param name="defaultValue">
        ///  (Optional) The default value.
        /// </param>
        /// <returns>
        ///  An ISchemaProperty.
        /// </returns>
        ///-------------------------------------------------------------------------------------------------
        protected ISchemaProperty DefineProperty(string name, ISchemaValueObject metadata, PropertyKind kind=PropertyKind.Normal, object defaultValue = null)
        {
            Contract.RequiresNotEmpty(name, "name");
            Contract.Requires(metadata, "metadata");

            var prop = new SchemaProperty(this, name, metadata, kind, defaultValue);
            return DefineProperty(prop);
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Adds a property.
        /// </summary>
        /// <exception cref="Exception">
        ///  Thrown when an exception error condition occurs.
        /// </exception>
        /// <param name="property">
        ///  The property definition.
        /// </param>
        /// <returns>
        ///  An ISchemaProperty.
        /// </returns>
        /// <exception cref="System.Exception">
        ///  Duplicate property name.
        /// </exception>
        ///-------------------------------------------------------------------------------------------------
        protected ISchemaProperty DefineProperty(ISchemaProperty property)
        {
            Contract.Requires(property, "property");

            lock (_propertiesByName)
            {
                if (GetProperty(property.Name) != null)
                    throw new Hyperstore.Modeling.MemoryStore.DuplicateElementException(ExceptionMessages.DuplicatePropertyName);

                _properties.Add(property);
                _propertiesByName.TryAdd(property.Name, property);
            }

            var trace = DomainModel.Resolve<IHyperstoreTrace>(false);
            if (trace != null)
                trace.WriteTrace(TraceCategory.Metadata, ExceptionMessages.CreatePropertyWithIdForMetaclassFormat, property.Name, property.Id, ((IModelElement)this).Id);

            var constraint = property.PropertySchema as Hyperstore.Modeling.Metadata.Constraints.IConstraint;
            if( constraint != null && this.Schema.Constraints is IConstraintManagerInternal)
            {
                (this.Schema.Constraints as IConstraintManagerInternal).AddConstraint(property, constraint);
            }
            return property;
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Defines the property.
        /// </summary>
        /// <exception cref="Exception">
        ///  Thrown when an exception error condition occurs.
        /// </exception>
        /// <typeparam name="T">
        ///  Type of the property.
        /// </typeparam>
        /// <param name="name">
        ///  The name.
        /// </param>
        /// <param name="defaultValue">
        ///  (Optional) The default value.
        /// </param>
        /// <param name="kind">
        ///  (Optional) the kind.
        /// </param>
        /// <returns>
        ///  An ISchemaProperty.
        /// </returns>
        ///-------------------------------------------------------------------------------------------------
        protected ISchemaProperty DefineProperty<T>(string name, object defaultValue = null, PropertyKind kind=PropertyKind.Normal)
        {
            Contract.RequiresNotEmpty(name, "name");

            var metadata = Store.GetSchemaInfo<T>(false);

            if (metadata == null && ReflectionHelper.IsEnum(typeof(T)))
                metadata = new EnumPrimitive(Schema, typeof(T));

            var mv = metadata as ISchemaValueObject;
            if (mv == null)
                throw new MetadataNotFoundException(ExceptionMessages.NoSchemaFoundForThisProperty);

            return DefineProperty(name, metadata as ISchemaValueObject, kind, defaultValue);
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Ensureses the metadata exists.
        /// </summary>
        /// <param name="domainModel">
        ///  The domain model.
        /// </param>
        /// <param name="name">
        ///  The name.
        /// </param>
        /// <returns>
        ///  An ISchemaElement.
        /// </returns>
        ///-------------------------------------------------------------------------------------------------
        protected override ISchemaElement EnsuresSchemaExists(IDomainModel domainModel, string name)
        {
            DebugContract.Requires(domainModel);
            DebugContract.RequiresNotEmpty(name);

            var type = GetType();
            if (type == typeof(SchemaEntity)) // Evite boucle infinie
                return domainModel.Store.PrimitivesSchema.SchemaEntitySchema;

            return base.EnsuresSchemaExists(domainModel, name);
        }

        private void EnsuresPropertiesLoaded()
        {
            if (!_propertiesLoaded)
            {
                lock (_propertiesByName)
                {
                    if (!_propertiesLoaded)
                    {
                        foreach (var p in _properties)
                        {
                            _propertiesByName.GetOrAdd(p.Name, p);
                        }
                        _propertiesLoaded = true;
                    }
                }
            }
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets the properties.
        /// </summary>
        /// <param name="recursive">
        ///  if set to <c>true</c> [recursive].
        /// </param>
        /// <returns>
        ///  An enumerator that allows foreach to be used to process the properties in this collection.
        /// </returns>
        ///-------------------------------------------------------------------------------------------------
        protected IEnumerable<ISchemaProperty> GetProperties(bool recursive)
        {
            EnsuresPropertiesLoaded();

            foreach (var p in _propertiesByName.Values)
            {
                yield return p;
            }

            if (recursive)
            {
                var superType = SuperClass;
                if (superType != null)
                {
                    foreach (var p in superType.GetProperties(true))
                    {
                        yield return p;
                    }
                }
            }
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets a property by name.
        /// </summary>
        /// <param name="name">
        ///  The property name.
        /// </param>
        /// <returns>
        ///  The property.
        /// </returns>
        ///-------------------------------------------------------------------------------------------------
        protected ISchemaProperty GetProperty(string name)
        {
            Contract.RequiresNotEmpty(name, "name");

            return GetProperties(true)
                    .FirstOrDefault(m => m.Name == name);
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Méthode appelée dans le processus de création que celle ci soit faite par new ou par
        ///  sérialisation.
        /// </summary>
        /// <param name="schemaElement">
        ///  The metadata.
        /// </param>
        /// <param name="domainModel">
        ///  The domain model.
        /// </param>
        ///-------------------------------------------------------------------------------------------------
        protected override void Initialize(ISchemaElement schemaElement, IDomainModel domainModel)
        {
            base.Initialize(schemaElement, domainModel);

            _superClassHandler = new ReferenceHandler(this, domainModel.Store.PrimitivesSchema.SchemaElementReferencesSuperElementSchema);
            _properties = new ModelElementCollection<ISchemaProperty>(this, domainModel.Store.PrimitivesSchema.SchemaElementHasPropertiesSchema);
            _propertiesByName = PlatformServices.Current.CreateConcurrentDictionary<string, ISchemaProperty>();
        }

        #endregion Methods of MetaClass (12)

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Releases the unmanaged resources used by the Hyperstore.Modeling.ModelElement and optionally
        ///  releases the managed resources.
        /// </summary>
        /// <param name="disposing">
        ///  true to release both managed and unmanaged resources; false to release only unmanaged
        ///  resources.
        /// </param>
        ///-------------------------------------------------------------------------------------------------
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            _propertiesByName = null;
            if (_properties != null)
                _properties.Dispose();
            if (_superClassHandler != null)
                _superClassHandler.Dispose();
        }

        object ISchemaInfo.Deserialize(SerializationContext ctx)
        {
            return Deserialize(ctx);
        }

        object ISchemaInfo.Serialize(object value)
        {
            return Serialize(value);
        }

        bool ISchemaInfo.IsA(ISchemaInfo metaClass)
        {
            return IsA(metaClass);
        }

        private string DebuggerDisplay
        {
            get { return String.Format("Name={0}, Id={1}", Name, ((IModelElement)this).Id); }
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Removes this instance.
        /// </summary>
        /// <exception cref="Exception">
        ///  Thrown when an exception error condition occurs.
        /// </exception>
        ///-------------------------------------------------------------------------------------------------
        protected override void Remove()
        {
            throw new ReadOnlyException(ExceptionMessages.CantRemoveSchemaElementSchemaIsImmutable);
        }

    }
}