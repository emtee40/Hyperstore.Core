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
using System.Linq;
using System.Threading;
using Hyperstore.Modeling.Commands;
using Hyperstore.Modeling.Events;

#endregion

namespace Hyperstore.Modeling
{
    /// <summary>
    ///     Wrapper en read only du résultat d'une session.
    ///     Cette classe est nécessaire car elle est utilisée par les événements.
    ///     Comme il est possible que des événements soient traités en asynchrone, la session initiale n'existera plus à ce
    ///     moment là il en faut
    ///     donc une copie.
    /// </summary>
    internal class SessionInformation : ISessionInformation
    {
        #region Fields

        private readonly Dictionary<string, object> _contextInfos;
        private readonly ISessionContext _context;
        #endregion

        #region Properties

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets the tracking data - All elements involved by the session.
        /// </summary>
        /// <value>
        ///  Information describing the tracking.
        /// </value>
        ///-------------------------------------------------------------------------------------------------
        public ISessionTrackingData TrackingData { get; private set; }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  ID of the store which has generated all the commands.
        /// </summary>
        /// <value>
        ///  The identifier of the origin store.
        /// </value>
        ///-------------------------------------------------------------------------------------------------
        public string OriginStoreId { get; private set; }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets a value indicating whether the session is read only.
        /// </summary>
        /// <value>
        ///  true if this instance is read only, false if not.
        /// </value>
        ///-------------------------------------------------------------------------------------------------
        public bool IsReadOnly { get; private set; }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets the cancellation token.
        /// </summary>
        /// <value>
        ///  The cancellation token.
        /// </value>
        ///-------------------------------------------------------------------------------------------------
        public CancellationToken CancellationToken { get; private set; }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets the session mode.
        /// </summary>
        /// <value>
        ///  The mode.
        /// </value>
        ///-------------------------------------------------------------------------------------------------
        public SessionMode Mode { get; private set; }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets a value indicating whether the session is aborted.
        /// </summary>
        /// <value>
        ///  true if this instance is aborted, false if not.
        /// </value>
        ///-------------------------------------------------------------------------------------------------
        public bool IsAborted { get; private set; }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Session has completed correctly
        /// </summary>
        /// <value>
        ///  true if succeed, false if not.
        /// </value>
        ///-------------------------------------------------------------------------------------------------
        public bool Succeed
        {
            get { return !(HasErrors || HasWarnings || IsAborted); }
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets the event list.
        /// </summary>
        /// <value>
        ///  The events.
        /// </value>
        ///-------------------------------------------------------------------------------------------------
        public IEnumerable<IEvent> Events { get; private set; }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets a value indicating whether this session is nested.
        /// </summary>
        /// <value>
        ///  true if this instance is nested, false if not.
        /// </value>
        ///-------------------------------------------------------------------------------------------------
        public bool IsNested { get; private set; }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets or sets a value indicating whether this instance has errors.
        /// </summary>
        /// <value>
        ///  true if this instance has errors, false if not.
        /// </value>
        ///-------------------------------------------------------------------------------------------------
        public bool HasErrors { get; private set; }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets or sets a value indicating whether this instance has warnings.
        /// </summary>
        /// <value>
        ///  true if this instance has warnings, false if not.
        /// </value>
        ///-------------------------------------------------------------------------------------------------
        public bool HasWarnings { get; private set; }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets the session id.
        /// </summary>
        /// <value>
        ///  The identifier of the session.
        /// </value>
        ///-------------------------------------------------------------------------------------------------
        public int SessionId { get; private set; }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets the store.
        /// </summary>
        /// <value>
        ///  The store.
        /// </value>
        ///-------------------------------------------------------------------------------------------------
        public IHyperstore Store { get; private set; }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets the default domain model.
        /// </summary>
        /// <value>
        ///  The default domain model.
        /// </value>
        ///-------------------------------------------------------------------------------------------------
        public IDomainModel DefaultDomainModel { get; private set; }

        #endregion

        #region Constructors

        internal SessionInformation(Session session, SessionLocalInfo info, ISessionTrackingData trackingData, IExecutionResultInternal messages)
        {
            DebugContract.Requires(session, "session");
            DebugContract.Requires(trackingData);

            _context = session.SessionContext;
            TrackingData = trackingData;
            CancellationToken = session.CancellationToken;
            IsAborted = session.IsAborted;
            IsNested = session.IsNested;
            Store = session.Store;
            IsReadOnly = session.IsReadOnly;
            Mode = info.Mode;
            OriginStoreId = info.OriginStoreId;
            SessionId = session.SessionId;
            DefaultDomainModel = info.DefaultDomainModel;
            _contextInfos = info.Infos;
            Events = session.Events.ToList();
            if (messages != null)
            {
                HasErrors = messages.HasErrors;
                HasWarnings = messages.HasWarnings;
            }
        }

        #endregion

        #region Methods

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Logs the given message.
        /// </summary>
        /// <param name="message">
        ///  The message.
        /// </param>
        ///-------------------------------------------------------------------------------------------------
        public void Log(DiagnosticMessage message)
        {
            Contract.Requires(message != null, "message");
            _context.Log(message);
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Gets the context info.
        /// </summary>
        /// <typeparam name="T">
        ///  Generic type parameter.
        /// </typeparam>
        /// <param name="key">
        ///  The key.
        /// </param>
        /// <returns>
        ///  The context information.
        /// </returns>
        ///-------------------------------------------------------------------------------------------------
        public T GetContextInfo<T>(string key)
        {
            Contract.RequiresNotEmpty(key, "key");

            object result;
            if (_contextInfos.TryGetValue(key, out result))
                return (T)result;

            return default(T);
        }

        ///-------------------------------------------------------------------------------------------------
        /// <summary>
        ///  Sets a value in the context info.
        /// </summary>
        /// <param name="key">
        ///  The key.
        /// </param>
        /// <param name="value">
        ///  The value.
        /// </param>
        ///-------------------------------------------------------------------------------------------------
        public void SetContextInfo(string key, object value)
        {
            Contract.RequiresNotEmpty(key, "key");

            if (value == null)
            {
                if (_contextInfos.ContainsKey(key))
                    _contextInfos.Remove(key);
            }
            else
                _contextInfos[key] = value;
        }

        #endregion
    }
}