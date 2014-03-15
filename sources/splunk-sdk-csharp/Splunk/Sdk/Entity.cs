﻿/*
 * Copyright 2014 Splunk, Inc.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"): you may
 * not use this file except in compliance with the License. You may obtain
 * a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

// TODO:
// [ ] Pick up standard properties from AtomEntry on Update, not just AtomEntry.Content
//     See [Splunk responses to REST operations](http://goo.gl/tyXDfs).
// [ ] Check for HTTP Status Code 204 (No Content) and empty atoms in 
//     Entity<TEntity>.UpdateAsync.
// [O] Contracts
//
// [ ] Documentation
//
// [ ] Remove Entity<TEntity>.Invalidate method
//     FJR: This gets called when we set the record value. Add a comment saying what it's
//     supposed to do when it's overridden.
//     DSN: I've adopted an alternative method for getting strongly-typed values. See, for
//     example, Job.DispatchState or ServerInfo.Guid.

namespace Splunk.Sdk
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Contracts;
    using System.Dynamic;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Xml.Linq;

    public class Entity<TEntity> where TEntity : Entity<TEntity>, new()
    {
        #region Constructors

        /// <summary>
        /// 
        /// </summary>
        public Entity()
        { }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <param name="namespace"></param>
        /// <param name="collection"></param>
        /// <param name="title"></param>
        protected Entity(Context context, Namespace @namespace, ResourceName collection, string title)
        {
            Contract.Requires<ArgumentNullException>(@namespace != null, "namespace");
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(title), "name");
            Contract.Requires<ArgumentException>(collection != null, "collection");
            Contract.Requires<ArgumentNullException>(context != null, "context");
            Contract.Requires(@namespace.IsSpecific);

            this.Context = context;

            this.Namespace = @namespace;
            this.Collection = collection;
            this.Title = title;

            this.ResourceName = new ResourceName(this.Collection, this.Title);
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the path to the collection containing the current <see cref=
        /// "Entity"/>.
        /// </summary>
        public ResourceName Collection
        { get; internal set; }

        /// <summary>
        /// Gets the <see cref="Context"/> instance for the current <see cref=
        /// "Entity"/>.
        /// </summary>
        public Context Context
        { get; internal set; }

        /// <summary>
		/// Gets the namespace containing the current <see cref="Entity"/>.
        /// </summary>
        public Namespace Namespace
        { get; private set; }

        /// <summary>
        /// Gets the resource name of the current <see cref="Entity"/>.
        /// </summary>
        /// <remarks>
        /// The resource name is the concatenation of <see cref=
        /// "Entity.Collection"/> and <see cref="Entity.Title"/>.
        /// </remarks>
        public ResourceName ResourceName
        { get; private set; }

        /// <summary>
        /// Gets the title of this <see cref="Entity"/>.
        /// </summary>
        public string Title
        { get; private set; }

        /// <summary>
        /// Gets the state of this <see cref="Entity"/>.
        /// </summary>
        public virtual dynamic Record
        { get; internal set; }

        #endregion

        #region Methods
        
        /// <summary>
        /// Gets a named item from an <see cref="IDictionary<string, object>"/>
        /// and applies a <see cref="ValueConverter"/>.
        /// </summary>
        /// <typeparam name="TValue">
        /// The type of value to return.
        /// </typeparam>
        /// <param name="record">
        /// The <see cref="ExpandoObject"/> containing the item identified by 
        /// <see cref="name"/>.
        /// </param>
        /// <param name="name">
        /// The name of the item to be returned.
        /// </param>
        /// <param name="valueConverter">
        /// The <see cref="ValueConverter"/> applied to the item identified by
        /// <see cref="name"/>.
        /// </param>
        /// <returns>
        /// A value of type <see cref="TValue"/>.
        /// </returns>
        /// <remarks>
        /// The value returned by this method is stored into <see cref="record"/>
        /// to reduce conversion overhead.
        /// </remarks>
        internal static TValue GetValue<TValue>(IDictionary<string, object> record, string name, ValueConverter<TValue> valueConverter)
        {
            Contract.Requires<InvalidOperationException>(record != null);
            Contract.Requires<ArgumentNullException>(name != null);
            Contract.Requires<ArgumentNullException>(valueConverter != null);

            object value;

            if (!record.TryGetValue(name, out value))
            {
                return valueConverter.DefaultValue;
            }

            if (value is TValue)
            {
                return (TValue)value;
            }

            var x = valueConverter.Convert(value);
            record[name] = x;

            return x;
        }

        internal TValue GetValue<TValue>(string name, ValueConverter<TValue> valueConverter)
        {
            return GetValue((IDictionary<string, object>)this.Record, name, valueConverter);
        }

        /// <summary>
        /// Gets the title of the current <see cref="Entity"/> from its atom entry.
        /// </summary>
        /// <returns>
        /// The title of the current <see cref="Entity"/>.
        /// </returns>
        /// <remarks>
        /// This method is overridden by the <see cref="Job"/> class. Its title
        /// comes from the <c>Sid</c> property, not the <c>Title</c> property of
        /// <see cref="Entity.Record"/>.
        /// </remarks>
        protected virtual string GetTitle()
        {
            Contract.Requires<InvalidOperationException>(this.Record != null);
            return this.Record.Title;
        }

        /// <summary>
        /// Refreshes the cached state of the current <see cref="Entity"/>.
        /// </summary>
        public async Task UpdateAsync()
        {
            // TODO: Parmeterized retry logic

            RequestException requestException = null;

			// FJR: I assume the retry logic is for jobs, since nothing else requires this. I suggest moving it
			// into Job. Also, it's insufficient. If you're just trying to get some state, this will do it, but
			// as of Splunk 6, getting a 200 and content back does not imply you have all the fields. For pivot
			// support, they're now shoving fields in as they become ready, so you have to wait until the dispatchState
			// field of the Atom entry reaches a certain point.
            for (int i = 3; i > 0 ; --i)
            {
                try
                {
                    // Gurantee: unique result because entities have specific namespaces

                    XDocument document = await this.Context.GetDocumentAsync(this.Namespace, this.ResourceName);
                    
                    if (document.Root.Name == AtomFeed.ElementName.Feed)
                    {
                        this.Record = new AtomFeed(document.Root).Entries[0].Content;
                    }
                    else
                    {
                        this.Record = new AtomEntry(document.Root).Content;
                    }
                    
                    return;
                }
                catch (RequestException e)
                {
                    if (e.StatusCode != System.Net.HttpStatusCode.NoContent)
                    {
                        throw;
                    }
                    requestException = e;
                }
                await Task.Delay(500);
            }

            throw requestException;
        }

        public override string ToString()
        {
            return string.Join("/", this.Context.ToString(), this.Namespace.ToString(), this.Collection.ToString(), this.Title);
        }

        #endregion

        #region Privates/internals

        internal static TEntity CreateEntity(Context context, ResourceName collection, AtomEntry entry)
        {
            Contract.Requires<ArgumentNullException>(collection != null, "collection");
            Contract.Requires<ArgumentNullException>(context != null, "context");
            Contract.Requires<ArgumentNullException>(entry != null, "entry");

            dynamic record = entry.Content;

            var entity = new TEntity()
            {
                Collection = collection,
                Context = context,
				Record = entry.Content,
            };

            entity.Title = entity.GetTitle();
            entity.ResourceName = new ResourceName(collection, entity.Title);
            entity.Namespace = new Namespace(record.Eai.Acl.Owner, record.Eai.Acl.App);

            return entity;
        }

		#endregion
    }
}
