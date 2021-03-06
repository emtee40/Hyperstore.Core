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
using System.Threading.Tasks;
using Hyperstore.Modeling.Commands;
using Hyperstore.Modeling.Container;
using System.Linq;
using Hyperstore.Modeling.Adapters;
using Hyperstore.Modeling.Traversal;

#endregion

namespace Hyperstore.Modeling.HyperGraph
{
    internal class HyperGraph : IHyperGraph, IDomainService, IIndexManager
    {
        private const string CONTEXT_KEY = "__MGA__";

        private Guid __id = Guid.NewGuid();

        #region Enums of HyperGraph (4)

        private readonly IServicesContainer _services;
        private IKeyValueStore _storage;
        private bool _disposed;
        private IDomainModel _domainModel;
        private IHyperstoreTrace _trace;
        private Hyperstore.Modeling.HyperGraph.Index.MemoryIndexManager _indexManager;
        private IGraphAdapter _loader;
        private ISupportsLazyLoading _lazyLoader;

        #endregion Enums of HyperGraph (4)

        #region Properties of HyperGraph (4)

        internal IIndexManager IndexManager { get { return _indexManager; } }

        private IHyperstore Store
        {
            get { return _domainModel.Store; }
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets the domain model.
        /// </summary>
        /// <value>
        ///  The domain model.
        /// </value>
        ///-------------------------------------------------------------------------------------------------
        public IDomainModel DomainModel
        {
            get { return _domainModel; }
        }

        #endregion Properties of HyperGraph (4)

        #region Constructors of HyperGraph (1)

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Constructor.
        /// </summary>
        /// <param name="services">
        ///  The services.
        /// </param>
        ///-------------------------------------------------------------------------------------------------
        public HyperGraph(IServicesContainer services)
        {
            Contract.Requires(services, "services");

            _services = services;
        }

        #endregion Constructors of HyperGraph (1)

        #region Methods of HyperGraph (21)

        public virtual bool IsDeleted(Identity id)
        {
            return false;
        }

        void IDomainService.SetDomain(IDomainModel domainModel)
        {
            Configure(domainModel);
        }

        protected void Configure(IDomainModel domainModel)
        {
            DebugContract.Requires(domainModel);
            if (_domainModel != null)
                return;

            _trace = domainModel.Resolve<IHyperstoreTrace>(false) ?? new EmptyHyperstoreTrace();
            _domainModel = domainModel;

            var kv = _services.Resolve<IKeyValueStore>() ?? new Hyperstore.Modeling.MemoryStore.TransactionalMemoryStore(domainModel);
            _storage = kv;
            if (kv is IDomainService)
                ((IDomainService)kv).SetDomain(domainModel);
            _indexManager = new Hyperstore.Modeling.HyperGraph.Index.MemoryIndexManager(this); // TODO lier avec TransactionalMemoryStore
            _loader = _services.Resolve<IGraphAdapter>();
            if (_loader is IDomainService)
                ((IDomainService)_loader).SetDomain(domainModel);
            _lazyLoader = _loader as ISupportsLazyLoading;
        }

        protected ITransaction BeginTransaction()
        {
            DebugContract.Requires(Session.Current);

            var tx = CurrentTransaction;
            if (tx == null)
            {
                tx = new HypergraphTransaction(_indexManager);
                CurrentTransaction = tx;
                CurrentTransaction.UpdateProfiler(p => p.NumberOfTransactions.Incr());
            }
            else
            {
                // Nested
                tx.PushNestedTransaction();
            }
            return tx;
        }

        internal HypergraphTransaction CurrentTransaction
        {
            get
            {
                var session = Session.Current;
                if (session == null)
                    throw new SessionRequiredException();

                var ctx = session.GetContextInfo<HypergraphTransaction>(CONTEXT_KEY);
                return ctx;
            }
            set
            {
                var session = Session.Current;
                if (session == null)
                    throw new SessionRequiredException();

                session.SetContextInfo(CONTEXT_KEY, value);
            }
        }
        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Adds the element.
        /// </summary>
        /// <param name="id">
        ///  .
        /// </param>
        /// <param name="schemaEntity">
        ///  The meta class.
        /// </param>
        /// <returns>
        ///  The new entity.
        /// </returns>
        ///-------------------------------------------------------------------------------------------------
        public virtual GraphNode CreateEntity(Identity id, ISchemaEntity schemaEntity)
        {
            DebugContract.Requires(id, "id");
            DebugContract.Requires(schemaEntity);

            if (Session.Current == null)
                throw new SessionRequiredException();

            Session.Current.AcquireLock(LockType.Exclusive, id);

            _trace.WriteTrace(TraceCategory.Hypergraph, "Add element {0}", id);

            using (var tx = BeginTransaction())
            {
                if (CurrentTransaction != null)
                {
                    CurrentTransaction.UpdateProfiler(p => p.NodesCreated.Incr());
                    CurrentTransaction.UpdateProfiler(p => p.NumberOfNodes.Incr());
                }

                var node = new GraphNode(id, schemaEntity.Id, NodeType.Node);
                _storage.AddNode(node);

                tx.Commit();
                return node;
            }
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Create a relationship.
        /// </summary>
        /// <exception cref="SessionRequiredException">
        ///  Thrown when a Session Required error condition occurs.
        /// </exception>
        /// <exception cref="InvalidElementException">
        ///  Thrown when an Invalid Element error condition occurs.
        /// </exception>
        /// <exception cref="HypergraphException">
        ///  Thrown when a Hypergraph error condition occurs.
        /// </exception>
        /// <param name="id">
        ///  .
        /// </param>
        /// <param name="metaRelationship">
        ///  The meta relationship.
        /// </param>
        /// <param name="startId">
        ///  The start identifier.
        /// </param>
        /// <param name="endId">
        ///  The end.
        /// </param>
        /// <returns>
        ///  The new relationship.
        /// </returns>
        ///-------------------------------------------------------------------------------------------------
        public virtual GraphNode CreateRelationship(Identity id, ISchemaRelationship metaRelationship, Identity startId, Identity endId)
        {
            DebugContract.Requires(id);
            DebugContract.Requires(metaRelationship);
            DebugContract.Requires(startId);
            DebugContract.Requires(endId);

            if (Session.Current == null)
                throw new SessionRequiredException();

            _trace.WriteTrace(TraceCategory.Hypergraph, "Add relationship {0} ({1}->{2})", id, startId, endId);

            Session.Current.AcquireLock(LockType.Exclusive, id);
            Session.Current.AcquireLock(LockType.Exclusive, startId);
            Session.Current.AcquireLock(LockType.Exclusive, endId);

            using (var tx = BeginTransaction())
            {
                var node = new GraphNode(id, metaRelationship.Id, NodeType.Edge, startId, endId);
                _storage.AddNode(node);

                var terminals = GetTerminalNodes(metaRelationship, startId, endId);
                var start = terminals.Item1;
                var end = terminals.Item2;

                if (start == null)
                    throw new InvalidElementException(startId);

                // Mise à jour des infos sur les relations propres à un noeud
                if (startId == endId)
                {
                    start = start.AddEdge(id, metaRelationship, Direction.Both, startId);
                    _storage.UpdateNode(start);
                }
                else
                {
                    start = start.AddEdge(id, metaRelationship, Direction.Outgoing, endId);
                    _storage.UpdateNode(start);

                    // Relation uni-directionnelle entre domaine.
                    if (end != null)
                    {
                        var tmp = end.AddEdge(id, metaRelationship, Direction.Incoming, startId);
                        if (tmp == null)
                            throw new HypergraphException(String.Format("Element {0} can not have multi parent", end.Id));
                        _storage.UpdateNode(tmp);
                    }
                }

                DeferAddIndex(metaRelationship, id);
                if (CurrentTransaction != null)
                {
                    // TODO arrive quand on met des données en cache qui ont été lues via un autre adapteur du coup
                    // si cela arrive ds une transaction la stat est fausse car ce n'est pas une véritable cr
                    CurrentTransaction.UpdateProfiler(p => p.RelationshipsCreated.Incr());
                    CurrentTransaction.UpdateProfiler(p => p.NumberOfEdges.Incr());
                }

                tx.Commit();
                return node;
            }
        }

        protected virtual Tuple<GraphNode, GraphNode> GetTerminalNodes(ISchemaRelationship schema, Identity startId, Identity endId)
        {
            var start = _storage.GetNode(startId) as GraphNode;

            // Si le noeud opposé se trouve dans un autre domaine, end sera null et le domaine cible ne sera pas
            // mis à jour. Seul le noeud source est impacté
            var end = startId.DomainModelName == endId.DomainModelName ? _storage.GetNode(endId) as GraphNode : null;

            return Tuple.Create(start, end);
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Peut-être une entity ou une relation.
        /// </summary>
        /// <param name="id">
        ///  .
        /// </param>
        /// <param name="schemaElement">
        ///  .
        /// </param>
        /// <returns>
        ///  The element.
        /// </returns>
        ///-------------------------------------------------------------------------------------------------
        public IModelElement GetElement(Identity id, ISchemaElement schemaElement)
        {
            Contract.Requires(id, "id");

            GraphNode v;
            if (!GetGraphNode(id, NodeType.EdgeOrNode, out v) || v == null)
                return null;

            var metadata = _domainModel.Store.GetSchemaElement(v.SchemaId);
            if (schemaElement != null && schemaElement.IsA(metadata))
                metadata = schemaElement;

            return (IModelElement)metadata.Deserialize(new SerializationContext(_domainModel, metadata, v));
        }

        internal virtual bool GetGraphNode(Identity id, NodeType nodeType, out GraphNode node)
        {
            node = _storage.GetNode(id);
            if (node == null && nodeType != NodeType.Property && _lazyLoader != null)
            {
                // Lazy loading
                LoadNodes(new Query { SingleId = id }, MergeOption.AppendOnly, _lazyLoader, true).Wait();
                node = _storage.GetNode(id);
            }

            return true; // it has not been deleted
        }

        internal virtual bool GraphNodeExists(Identity id)
        {
            var exists = _storage.Exists(id);
            if (exists == false && _lazyLoader != null)
            {
                // Lazy loading
                LoadNodes(new Query { SingleId = id }, MergeOption.AppendOnly, _lazyLoader, true).Wait();
            }
            return exists;
        }

        internal virtual IEnumerable<GraphNode> GetGraphNodes(NodeType nodetype)
        {
            if (_lazyLoader != null)
                LoadNodes(new Query { NodeType = nodetype }, MergeOption.AppendOnly, _lazyLoader, true).Wait();
            return _storage.GetAllNodes(nodetype);
        }

        internal virtual IEnumerable<EdgeInfo> GetGraphEdges(GraphNode source, Direction direction)
        {
            var node = source as GraphNode;
            return direction == Direction.Incoming ? node.Incomings : node.Outgoings;
        }


        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets the elements.
        /// </summary>
        /// <param name="metadata">
        ///  The metadata.
        /// </param>
        /// <param name="skip">
        ///  The skip.
        /// </param>
        /// <returns>
        ///  An enumerator that allows foreach to be used to process the entities in this collection.
        /// </returns>
        ///-------------------------------------------------------------------------------------------------
        public IEnumerable<IModelEntity> GetEntities(ISchemaEntity metadata, int skip = 0)
        {
            return GetEntities<IModelEntity>(metadata, skip);
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets the element or relationships.
        /// </summary>
        /// <param name="metadata">
        ///  The metadata.
        /// </param>
        /// <param name="skip">
        ///  The skip.
        /// </param>
        /// <returns>
        ///  An enumerator that allows foreach to be used to process the elements in this collection.
        /// </returns>
        ///-------------------------------------------------------------------------------------------------
        public IEnumerable<IModelElement> GetElements(ISchemaElement metadata, int skip = 0)
        {
            var query = GetGraphNodes(NodeType.EdgeOrNode);
            return GetElementsCore<IModelElement>(query, metadata, skip);
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets the elements.
        /// </summary>
        /// <typeparam name="T">
        ///  Generic type parameter.
        /// </typeparam>
        /// <param name="metadata">
        ///  The metadata.
        /// </param>
        /// <param name="skip">
        ///  The skip.
        /// </param>
        /// <returns>
        ///  An enumerator that allows foreach to be used to process the entities in this collection.
        /// </returns>
        ///-------------------------------------------------------------------------------------------------
        public IEnumerable<T> GetEntities<T>(ISchemaEntity metadata, int skip = 0) where T : IModelEntity
        {
            var query = GetGraphNodes(NodeType.Node);
            return GetElementsCore<T>(query, metadata, skip);
        }

        protected IEnumerable<T> GetElementsCore<T>(IEnumerable<GraphNode> query, ISchemaElement metadata, int skip) where T : IModelElement
        {
            ISchemaElement currentMetadata = null;
            var cx = 0;
            foreach (var e in query)
            {
                if (e == null)
                    continue;

                if (currentMetadata == null || currentMetadata.Id != e.SchemaId)
                    currentMetadata = _domainModel.Store.GetSchemaElement(e.SchemaId);

                if (metadata == null || currentMetadata.IsA(metadata))
                {
                    if (cx++ >= skip)
                    {
                        var ctx = new SerializationContext(_domainModel, currentMetadata, e);
                        yield return (T)currentMetadata.Deserialize(ctx);
                    }
                }
            }
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets the relationships.
        /// </summary>
        /// <param name="metadata">
        ///  The metadata.
        /// </param>
        /// <param name="start">
        ///  The start.
        /// </param>
        /// <param name="end">
        ///  The end.
        /// </param>
        /// <param name="skip">
        ///  The skip.
        /// </param>
        /// <returns>
        ///  An enumerator that allows foreach to be used to process the relationships in this collection.
        /// </returns>
        ///-------------------------------------------------------------------------------------------------
        public IEnumerable<IModelRelationship> GetRelationships(ISchemaRelationship metadata, IModelElement start, IModelElement end, int skip = 0)
        {
            return GetRelationships<IModelRelationship>(metadata, start, end, skip);
        }

        protected IEnumerable<EdgeInfo> GetEdges(Identity sourceId, Direction direction, ISchemaRelationship metadata, Identity oppositeId = null)
        {
            DebugContract.Requires(sourceId);

            GraphNode v;
            if (!GetGraphNode(sourceId, NodeType.EdgeOrNode, out v) || v == null)
                return Enumerable.Empty<EdgeInfo>();

            return GetEdgesCore(v, direction, metadata, oppositeId);
        }

        protected IEnumerable<EdgeInfo> GetEdgesCore(GraphNode source, Direction direction, ISchemaRelationship metadata, Identity oppositeId = null)
        {
            if (source == null)
                yield break;

            if ((direction & Direction.Outgoing) == Direction.Outgoing)
            {
                foreach (var info in GetGraphEdges(source, Direction.Outgoing))
                {
                    if (oppositeId == null || info.EndId == oppositeId)
                    {
                        if (metadata != null)
                        {
                            if (info.SchemaId != metadata.Id)
                            {
                                var m = _domainModel.Store.GetSchemaRelationship(info.SchemaId);
                                if (!m.IsA(metadata))
                                    continue;
                            }
                        }
                        yield return info;
                    }
                }
            }

            if ((direction & Direction.Incoming) == Direction.Incoming)
            {
                foreach (var info in GetGraphEdges(source, Direction.Incoming))
                {
                    if (oppositeId == null || info.EndId == oppositeId)
                    {
                        if (metadata != null)
                        {
                            if (info.SchemaId != metadata.Id)
                            {
                                var m = _domainModel.Store.GetSchemaRelationship(info.SchemaId);
                                if (!m.IsA(metadata))
                                    continue;
                            }
                        }
                        yield return info;
                    }
                }
            }
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets the relationships.
        /// </summary>
        /// <typeparam name="T">
        ///  Generic type parameter.
        /// </typeparam>
        /// <param name="metadata">
        ///  The metadata.
        /// </param>
        /// <param name="start">
        ///  The start.
        /// </param>
        /// <param name="end">
        ///  The end.
        /// </param>
        /// <param name="skip">
        ///  The skip.
        /// </param>
        /// <returns>
        ///  An enumerator that allows foreach to be used to process the relationships in this collection.
        /// </returns>
        ///-------------------------------------------------------------------------------------------------
        public IEnumerable<T> GetRelationships<T>(ISchemaRelationship metadata, IModelElement start, IModelElement end, int skip = 0) where T : IModelRelationship
        {
            IEnumerable<NodeInfo> query;
            if (start != null)
            {
                query = GetEdges(start.Id, Direction.Outgoing, metadata, end != null ? end.Id : null);
                return GetRelationshipsCore<T>(query, skip, metadata);
            }
            else if (end != null)
            {
                query = GetEdges(end.Id, Direction.Incoming, metadata);
                return GetRelationshipsCore<T>(query, skip, metadata);
            }

            query = GetGraphNodes(NodeType.Edge);
            return GetRelationshipsCore<T>(query, skip, metadata);
        }

        protected IEnumerable<T> GetRelationshipsCore<T>(IEnumerable<NodeInfo> query, int skip, ISchemaRelationship metadata) where T : IModelRelationship
        {
            var cx = 0;
            var currentMetadata = metadata;
            foreach (var edge in query)
            {
                if (edge == null)
                    continue;

                if (cx++ < skip)
                    continue;

                GraphNode node;
                if (!GetGraphNode(edge.Id, NodeType.Edge, out node) || node == null)
                    continue;

                if (currentMetadata == null || edge.SchemaId != currentMetadata.Id)
                    currentMetadata = _domainModel.Store.GetSchemaRelationship(edge.SchemaId);
                var ctx = new SerializationContext(_domainModel, currentMetadata, node);
                yield return (T)currentMetadata.Deserialize(ctx);
            }
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Removes an entity
        /// </summary>
        /// <exception cref="InvalidElementException">
        ///  Thrown when an Invalid Element error condition occurs.
        /// </exception>
        /// <param name="id">
        ///  entity id
        /// </param>
        /// <param name="throwExceptionIfNotExists">
        ///  true to throw exception if not exists.
        /// </param>
        /// <returns>
        ///  the schema element of the removed entity or null
        /// </returns>
        ///-------------------------------------------------------------------------------------------------
        public virtual ISchemaEntity RemoveEntity(Identity id, bool throwExceptionIfNotExists)
        {
            DebugContract.Requires(id);
            DebugContract.Requires(Session.Current);

            Session.Current.AcquireLock(LockType.Exclusive, id);

            GraphNode node;
            if (!GetGraphNode(id, NodeType.Node, out node))
            {
                if (!throwExceptionIfNotExists)
                    return null;

                throw new InvalidElementException(id);
            }

            var schemaEntity = this._domainModel.Store.GetSchemaEntity(node.SchemaId);
            RemoveDependencies(node);

            _trace.WriteTrace(TraceCategory.Hypergraph, "Remove element {0}", id);
            RemoveProperties(id, schemaEntity);

            using (var tx = BeginTransaction())
            {
                if (_storage.RemoveNode(node.Id))
                {
                    foreach (var prop in schemaEntity.GetProperties(true))
                    {
                        _storage.RemoveNode(node.Id.CreateAttributeIdentity(prop.Name));
                    }

                    // Informe qu'il faudra mettre à jour les index
                    DeferRemoveIndex(schemaEntity, node.Id);

                    CurrentTransaction.UpdateProfiler(p => p.NodesDeleted.Incr());
                    CurrentTransaction.UpdateProfiler(p => p.NumberOfNodes.Dec());
                }
                tx.Commit();
            }

            return schemaEntity;
        }

        private void RemoveDependencies(GraphNode node)
        {
            if ((Session.Current.Mode & SessionMode.UndoOrRedo) == 0 && Session.Current.GetContextInfo<bool>("$$remove$$") == false)
            {
                var visitor = new DeleteDependencyVisitor();
                _domainModel.Traversal.WithVisitor(visitor).Traverse(node);
                List<IDomainCommand> commands = null;

                foreach (var incoming in node.Incomings)
                {
                    if (commands == null)
                        commands = new List<IDomainCommand>();
                    commands.Add(new RemoveRelationshipCommand(this._domainModel, incoming.Id));
                }

                if (commands != null || visitor.Commands.Any())
                {
                    var cmds = commands != null ? visitor.Commands.Concat(commands) : visitor.Commands;

                    using (var session = _domainModel.Store.BeginSession())
                    {
                        session.SetContextInfo("$$remove$$", true); // loop guard
                        session.Execute(cmds.ToArray());
                        session.AcceptChanges();
                    }
                }
            }
        }

        private void RemoveProperties(Identity id, ISchemaElement schemaEntity)
        {
            List<IDomainCommand> commands = new List<IDomainCommand>();

            // Suppression des propriétés 
            foreach (var prop in schemaEntity.GetProperties(true))
            {
                // Pour chaque propriété qui n'est pas une relation, on va générer un événement spécifique
                if (prop.PropertySchema is ISchemaRelationship)
                    continue;

                var pnode = GetPropertyValue(id, prop);
                if (pnode != null)
                {
                    _trace.WriteTrace(TraceCategory.Hypergraph, "Remove property {0}.{1}", id, prop.Name);
                    commands.Add(new RemovePropertyCommand(_domainModel, id, schemaEntity.Id, prop));
                }
            }
            if (commands.Count > 0)
            {
                Session.Current.Execute(commands.ToArray());
            }
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Removes the relationship.
        /// </summary>
        /// <exception cref="InvalidElementException">
        ///  Thrown when an Invalid Element error condition occurs.
        /// </exception>
        /// <param name="id">
        ///  relationship id
        /// </param>
        /// <param name="throwExceptionIfNotExists">
        ///  true to throw exception if not exists.
        /// </param>
        /// <returns>
        ///  the schema element of the removed entity or null
        /// </returns>
        ///-------------------------------------------------------------------------------------------------
        public virtual ISchemaRelationship RemoveRelationship(Identity id, bool throwExceptionIfNotExists)
        {
            DebugContract.Requires(id);
            DebugContract.Requires(Session.Current);

            Session.Current.AcquireLock(LockType.Exclusive, id);

            GraphNode edge;
            if (!GetGraphNode(id, NodeType.Node, out edge))
            {
                if (!throwExceptionIfNotExists)
                    return null;
                throw new InvalidElementException(id);
            }

            var schemaRelationship = this._domainModel.Store.GetSchemaRelationship(edge.SchemaId);

            Session.Current.AcquireLock(LockType.Exclusive, edge.StartId);
            Session.Current.AcquireLock(LockType.Exclusive, edge.EndId);

            RemoveDependencies(edge);

            _trace.WriteTrace(TraceCategory.Hypergraph, "Remove relationship {0}", id);
            RemoveProperties(id, schemaRelationship);

            using (var tx = BeginTransaction())
            {
                if (_storage.RemoveNode(id))
                {
                    var terminals = GetTerminalNodes(schemaRelationship, edge.StartId, edge.EndId);
                    var start = terminals.Item1;
                    var end = terminals.Item2;

                    if (start == null && throwExceptionIfNotExists)
                        throw new InvalidElementException(edge.StartId);

                    // Si le noeud opposé se trouve dans un autre domaine, on ne le met pas à jour
                    // Seul le noeud source est impacté
                    if (edge.StartId.DomainModelName == edge.EndId.DomainModelName)
                    {
                        if (end == null && throwExceptionIfNotExists)
                            throw new InvalidElementException(edge.EndId);
                    }

                    // Mise à jour des infos sur les relations propres à un noeud
                    if (edge.StartId == edge.EndId)
                    {
                        if (start != null)
                        {
                            start = start.RemoveEdge(edge.Id, Direction.Both);
                            _storage.UpdateNode(start);
                        }
                    }
                    else
                    {
                        if (start != null)
                        {
                            start = start.RemoveEdge(edge.Id, Direction.Outgoing);
                            _storage.UpdateNode(start);
                        }

                        // Relation uni-directionnelle entre domaine.
                        if (end != null)
                        {
                            end = end.RemoveEdge(edge.Id, Direction.Incoming);
                            _storage.UpdateNode(end);
                        }
                    }

                    foreach (var prop in schemaRelationship.GetProperties(true))
                    {
                        _storage.RemoveNode(edge.Id.CreateAttributeIdentity(prop.Name));
                    }

                    DeferRemoveIndex(schemaRelationship, edge.Id);

                    CurrentTransaction.UpdateProfiler(p => p.RelationshipsDeleted.Incr());
                    CurrentTransaction.UpdateProfiler(p => p.NumberOfEdges.Dec());
                }
                tx.Commit();
            }

            return schemaRelationship;
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets the attribute.
        /// </summary>
        /// <exception cref="InvalidElementException">
        ///  Thrown when an Invalid Element error condition occurs.
        /// </exception>
        /// <param name="ownerId">
        ///  The identifier that owns this item.
        /// </param>
        /// <param name="property">
        ///  The property.
        /// </param>
        /// <returns>
        ///  The property value or null if not exists.
        /// </returns>
        ///-------------------------------------------------------------------------------------------------
        public PropertyValue GetPropertyValue(Identity ownerId, ISchemaProperty property)
        {
            DebugContract.Requires(ownerId);
            DebugContract.Requires(property);

            GraphNode v;
            if (!GraphNodeExists(ownerId))
                throw new InvalidElementException(ownerId);

            var pid = ownerId.CreateAttributeIdentity(property.Name);

            if (!GetGraphNode(pid, NodeType.Property, out v) || v == null)
                return null;

            var p = v as GraphNode;
            Debug.Assert(p != null);

            return new PropertyValue
            {
                Value = p.Value,
                CurrentVersion = p.Version
            };
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Sets the attribute.
        /// </summary>
        /// <param name="owner">
        ///  The owner.
        /// </param>
        /// <param name="property">
        ///  The property.
        /// </param>
        /// <param name="value">
        ///  The value.
        /// </param>
        /// <param name="version">
        ///  [out] The version.
        /// </param>
        /// <returns>
        ///  true if it succeeds, false if it fails.
        /// </returns>
        ///-------------------------------------------------------------------------------------------------
        public virtual PropertyValue SetPropertyValue(IModelElement owner, ISchemaProperty property, object value, long? version)
        {
            return SetPropertyValueCore(owner, property, value, version, null);
        }

        protected PropertyValue SetPropertyValueCore(IModelElement owner, ISchemaProperty property, object value, long? version, GraphNode oldNode)
        {
            DebugContract.Requires(owner);
            DebugContract.Requires(property);
            DebugContract.Requires(Session.Current);

            _trace.WriteTrace(TraceCategory.Hypergraph, "{0}.{1} = {2}", owner, property.Name, value);
            using (var tx = BeginTransaction())
            {
                // Vérification si le owner existe
                if (!GraphNodeExists(owner.Id))
                    throw new InvalidElementException(owner.Id);

                var pid = owner.Id.CreateAttributeIdentity(property.Name);

                // Recherche si l'attribut existe
                var pnode = _storage.GetNode(pid) as GraphNode;
                if (pnode == null)
                {
                    // N'existe pas encore. On crée l'attribut et une relation avec son owner
                    pnode = new GraphNode(pid, property.Id, NodeType.Property, value: value, version: version);
                    _storage.AddNode(pnode, owner.Id);
                    DeferAddIndex(owner.SchemaInfo, owner.Id, property.Name, value);
                    tx.Commit();

                    var oldPropertyNode = oldNode as GraphNode;
                    return new PropertyValue { Value = value, OldValue = oldPropertyNode != null ? oldPropertyNode.Value : property.DefaultValue, CurrentVersion = pnode.Version };
                }

                var oldValue = pnode.Value;
                // TODO
                //if (version != null && pnode.Version != version)
                //{
                //    throw new ConflictException(ownerId, ownerMetadata, property, value, oldValue, version.Value, pnode.Version);
                //}

                if (Equals(oldValue, value))
                {
                    tx.Commit();
                    return new PropertyValue { Value = value, OldValue = oldValue, CurrentVersion = pnode.Version };
                }

                DeferRemoveIndex(owner.SchemaInfo, owner.Id, property.Name, oldValue);

                pnode = pnode.SetValue(value);
                _storage.UpdateNode(pnode);
                DeferAddIndex(owner.SchemaInfo, owner.Id, property.Name, value);

                tx.Commit();
                return new PropertyValue { Value = value, OldValue = oldValue, CurrentVersion = pnode.Version };
            }
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Creates the index.
        /// </summary>
        /// <exception cref="NotImplementedException">
        ///  Thrown when the requested operation is unimplemented.
        /// </exception>
        /// <param name="metaclass">
        ///  .
        /// </param>
        /// <param name="name">
        ///  The name.
        /// </param>
        /// <param name="unique">
        ///  true to unique.
        /// </param>
        /// <param name="propertyNames">
        ///  List of names of the properties.
        /// </param>
        /// <returns>
        ///  The new index.
        /// </returns>
        ///-------------------------------------------------------------------------------------------------
        public IIndex CreateIndex(ISchemaElement metaclass, string name, bool unique, params string[] propertyNames)
        {
            DebugContract.Requires(metaclass);
            DebugContract.RequiresNotEmpty(name);

            return _indexManager.CreateIndex(metaclass, name, unique, propertyNames);
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Drops the index.
        /// </summary>
        /// <exception cref="NotImplementedException">
        ///  Thrown when the requested operation is unimplemented.
        /// </exception>
        /// <param name="name">
        ///  The name.
        /// </param>
        ///-------------------------------------------------------------------------------------------------
        public void DropIndex(string name)
        {
            DebugContract.RequiresNotEmpty(name);

            _indexManager.DropIndex(name);
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Retourne un index.
        /// </summary>
        /// <param name="name">
        ///  The name.
        /// </param>
        /// <returns>
        ///  The index.
        /// </returns>
        ///-------------------------------------------------------------------------------------------------
        public IIndex GetIndex(string name)
        {
            DebugContract.RequiresNotEmpty(name);

            return _indexManager.GetIndex(name);
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged
        ///  resources.
        /// </summary>
        ///-------------------------------------------------------------------------------------------------
        public void Dispose()
        {
            if (_disposed)
                return;

            if (_storage is IDisposable)
                ((IDisposable)_storage).Dispose();

            if (_loader is IDisposable)
                ((IDisposable)_loader).Dispose();

            _loader = null;
            _storage = null;
            _disposed = true;
            _domainModel = null;
        }

        internal void DeferRemoveIndex(ISchemaElement metaclass, Identity id, string propertyName = null, object key = null)
        {
            DebugContract.Requires(metaclass);
            DebugContract.Requires(id);

            var tx = CurrentTransaction;
            if (tx != null)
                tx.RemoveFromIndex(metaclass, id, propertyName, key);
        }

        internal void DeferAddIndex(ISchemaElement metaclass, Identity id, string propertyName = null, object key = null)
        {
            DebugContract.Requires(metaclass);
            DebugContract.Requires(id);

            var tx = CurrentTransaction;
            if (tx != null)
                tx.AddToIndex(metaclass, id, propertyName, key);
        }

        public Task<int> LoadNodes(Query query, MergeOption option, IGraphAdapter adapter, bool lazyLoading)
        {
            if (adapter == null)
                adapter = _loader;

            var tcs = new TaskCompletionSource<int>();
            var cx = 0;
            if (adapter == null)
            {
                tcs.TrySetResult(cx);
                return tcs.Task;
            }
            var oldLazyLoader = _lazyLoader;
            try
            {
                // Disable lazy loading
                _lazyLoader = null;

                var q = adapter is ISupportsLazyLoading && lazyLoading ? ((ISupportsLazyLoading)adapter).LazyLoadingNodes(query) : adapter.LoadNodes(query);
                using (var session = this.Store.BeginSession(new SessionConfiguration { Mode = SessionMode.Loading | SessionMode.SkipConstraints }))
                {
                    foreach (var result in q)
                    {
                        if (Session.Current != null && Session.Current.TrackingData.GetTrackedElementState(result.Id) == TrackingState.Removed)
                            continue;
                        cx++;
                        var newInCache = false;
                        var nodeMetaclass = result.SchemaInfo;

                        // Si ce noeud n'existe pas dans le cache, on le met
                        GraphNode graphNode;
                        GetGraphNode(result.Id, result.NodeType, out graphNode);
                        var node = graphNode as GraphNode;
                        if (node == null)
                        {
                            if (result.NodeType == NodeType.Edge)
                            {
                                var rSchema = nodeMetaclass as ISchemaRelationship;
                                node = CreateRelationship(result.Id,
                                                          rSchema,
                                                          result.StartId,
                                                          result.EndId
                                                         ) as GraphNode;
                            }
                            else
                            {
                                node = CreateEntity(result.Id, nodeMetaclass as ISchemaEntity) as GraphNode;
                            }
                            newInCache = true;
                        }

                        if (option == MergeOption.AppendOnly && newInCache || option == MergeOption.OverwriteChanges)
                        {
                            foreach (var edge in result.Outgoings)
                            {
                                var edgeSchema = _domainModel.Store.GetSchemaRelationship(edge.SchemaId);
                                node = node.AddEdge(edge.Id, edgeSchema, Direction.Outgoing, edge.EndId);
                            }
                            foreach (var edge in result.Incomings)
                            {
                                var edgeSchema = _domainModel.Store.GetSchemaRelationship(edge.SchemaId);
                                node = node.AddEdge(edge.Id, edgeSchema, Direction.Incoming, edge.EndId);
                            }
                        }

                        var ctx = new SerializationContext(_domainModel, result.SchemaInfo, result);
                        var mel = (IModelElement)result.SchemaInfo.Deserialize(ctx);
                        if (mel != null)
                        {
                            if (result.Properties != null)
                            {
                                foreach (var property in result.Properties)
                                {
                                    // Mise à jour des propriétés lues
                                    if (option == MergeOption.AppendOnly && newInCache || option == MergeOption.OverwriteChanges)
                                        SetPropertyValue(mel, property.Key, property.Value.Value, property.Value.CurrentVersion);
                                    else if (option == MergeOption.PreserveChanges)
                                    {
                                        if (GetPropertyValue(node.Id, property.Key) == null)
                                            SetPropertyValue(mel, property.Key, property.Value.Value, property.Value.CurrentVersion);
                                    }
                                }
                            }
                        }
                    }
                    session.AcceptChanges();
                }
                tcs.TrySetResult(cx);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
            finally
            {
                _lazyLoader = oldLazyLoader;
            }

            return tcs.Task;
        }
        #endregion Methods of HyperGraph (21)
    }
}