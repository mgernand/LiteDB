﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteDB
{
    public partial class LiteEngine
    {
        /// <summary>
        /// Find for documents in a collection using Query definition
        /// </summary>
        public IEnumerable<BsonDocument> Find(string collection, Query query, int skip = 0, int limit = int.MaxValue)
        {
            if (collection.IsNullOrWhiteSpace()) throw new ArgumentNullException("collection");
            if (query == null) throw new ArgumentNullException("query");

            using (_locker.Read())
            {
                // get my collection page
                var col = this.GetCollectionPage(collection, false);

                // no collection, no documents
                if (col == null) yield break;

                // get nodes from query executor to get all IndexNodes
                var nodes = query.Run(col, _indexer);

                // if query run on index, lets skip/take with linq-to-object
                if (query.Mode == QueryMode.IndexSeek)
                {
                    if (skip > 0) nodes = nodes.Skip(skip);

                    if (limit != int.MaxValue) nodes = nodes.Take(limit);
                }

                _log.Write(Logger.QUERY, "executing query in {0} :: {1}", query.Mode, query);

                // for each document, read data and deserialize as document
                foreach (var node in nodes)
                {
                    _log.Write(Logger.QUERY, "read document on '{0}' :: _id = {1}", collection, node.Key);

                    byte[] buffer;
                    BsonDocument doc;

                    // encapsulate read operation inside a try/catch (yield do not support try/catch)
                    buffer = _data.Read(node.DataBlock);
                    doc = BsonSerializer.Deserialize(buffer).AsDocument;

                    // if need run in full scan, execute full scan and test return
                    if (query.Mode == QueryMode.FullScan)
                    {
                        // execute query condition here - if false, do not add on final results
                        if (query.ExecuteFullScan(doc) == false) continue;

                        // implement skip/limit before on full search - no linq
                        if (--skip >= 0) continue;

                        if (--limit <= -1) yield break;
                    }

                    yield return doc;

                    _trans.CheckPoint();
                }
            }
        }

        /// <summary>
        /// Find index keys from collection. Do not retorn document, only key value
        /// </summary>
        public IEnumerable<BsonValue> FindIndex(string collection, Query query, int skip = 0, int limit = int.MaxValue)
        {
            if (collection.IsNullOrWhiteSpace()) throw new ArgumentNullException("collection");
            if (query == null) throw new ArgumentNullException("query");

            using (_locker.Read())
            {
                // get my collection page
                var col = this.GetCollectionPage(collection, false);

                // no collection, no values
                if (col == null) yield break;

                // get nodes from query executor to get all IndexNodes
                var nodes = query.Run(col, _indexer);

                // skip first N nodes
                if (skip > 0) nodes = nodes.Skip(skip);

                // limit in M nodes
                if (limit != int.MaxValue) nodes = nodes.Take(limit);

                // for each document, read data and deserialize as document
                foreach (var node in nodes)
                {
                    _log.Write(Logger.QUERY, "read index key on '{0}' :: key = {1}", collection, node.Key);

                    _trans.CheckPoint();

                    yield return node.Key;
                }
            }
        }

        #region FindOne/FindById

        /// <summary>
        /// Find first or default document based in collection based on Query filter
        /// </summary>
        public BsonDocument FindOne(string collection, Query query)
        {
            return this.Find(collection, query).FirstOrDefault();
        }

        /// <summary>
        /// Find first or default document based in _id field
        /// </summary>
        public BsonDocument FindById(string collection, BsonValue id)
        {
            if (id == null || id.IsNull) throw new ArgumentNullException("id");

            return this.Find(collection, Query.EQ("_id", id)).FirstOrDefault();
        }


        /// <summary>
        /// Returns all documents inside collection order by _id index.
        /// </summary>
        public IEnumerable<BsonDocument> FindAll(string collection)
        {
            return this.Find(collection, Query.All());
        }

        #endregion
    }
}