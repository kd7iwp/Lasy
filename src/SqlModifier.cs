﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data.SqlClient;
using Nvelope;
using Nvelope.Reactive;
using Nvelope.Reflection;
using System.Reactive.Linq;
using System.Reactive;
using System.Data;

namespace Lasy
{
    public abstract class SqlModifier : SqlAnalyzer, IDBModifier
    {
        public SqlModifier(string connectionString, TimeSpan cacheDuration = default(TimeSpan))
            : base(connectionString, cacheDuration)
        {
            if (cacheDuration == default(TimeSpan))
                cacheDuration = _defaultCacheDuration();

            // We'll need to change the base class' underlying _getPrimaryKeys, etc methods
            // Right now, they cache the results. However, if they cache en empty PK list for
            // a table that doesn't exist, and then we create the table, we want to clear that
            // cache so it gets the updated values.
            // We can do that by replacing the function implementation with our own.
            // We want to use the base class implementation unless we know that we've created
            // the table - if we have, we want to make sure we clear the cache and go
            // down to the database to get it

            // Ok, this is some cool magic from the System.Reactive lib by MS
            // We create an IObservable<string> that encapsulates our stream of table
            // creation events. The, we can pass that stream to Memoize to allow it to 
            // invalidate the cache. 
            // Semantically, this is ... = Observable.FromEvent(TableCreated), but unfortunately
            // events aren't first-class in C#, so the syntax doesn't allow for this. The line below 
            // is as good as they could make it.
            var tableCreatedStream = Observable.FromEvent<string>(del => TableCreated += del, del => TableCreated -= del);

            _getFields = new Func<string, ICollection<string>>(_getFieldsFromDB).Memoize(cacheDuration, tableCreatedStream);
            _getAutonumberKey = new Func<string, string>(_getAutonumberKeyFromDB).Memoize(cacheDuration, tableCreatedStream);
            _getPrimaryKeys = new Func<string, ICollection<string>>(_getPrimaryKeysFromDB).Memoize(cacheDuration, tableCreatedStream);
            _tableExists = new Func<string, bool>(_tableExistsFromDB).Memoize(cacheDuration, tableCreatedStream);
        }

        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="schema"></param>
        /// <param name="table"></param>
        /// <param name="fields">A mapping of the fieldname to .NET type of the fields</param>
        /// <returns></returns>
        protected abstract string _getCreateTableSql(string schema, string table, Dictionary<string, object> fields);

        protected abstract string _getSchemaExistsSql();
        protected abstract string _getCreateSchemaSql(string schema);

        protected Action<string> _createTable;

        /// <summary>
        /// Fired every time a table is created
        /// </summary>
        public event Action<string> TableCreated;
        /// <summary>
        /// Fired every time a schema is created
        /// </summary>
        public event Action<string> SchemaCreated;

        public void CreateTable(string tablename, Dictionary<string, object> fields)
        {
            var table = _tableOnly(tablename);
            var schema = _schemaOnly(tablename);
            var sql = _getCreateTableSql(schema, table, fields);
            var paras = new { table = table, schema = schema };

            if (!SchemaExists(schema))
                CreateSchema(schema);

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Execute(sql, paras);
            }
            if(TableCreated != null)
                TableCreated(tablename);
        }

        public virtual bool SchemaExists(string schema)
        {
            var paras = new { schema = schema };
            var sql = _getSchemaExistsSql();
            using (var conn = new SqlConnection(_connectionString))
            {
                return conn.ExecuteSingleValueOr(false, sql, paras);
            }
        }

        public virtual void CreateSchema(string schema)
        {
            var sql = _getCreateSchemaSql(schema);
            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Execute(sql);
            }
            if(SchemaCreated != null)
                SchemaCreated(schema);
        }

        protected string guessCharSizeNeeded(object o)
        {
            var s = o as string;
            if (s.IsNullOrEmpty())
                return "100";

            // If it has just one word in it, make it 100
            if (s.Tokenize().Count() <= 1)
                return "100";

            if (s.Length < 20)
                return "100";

            if (s.Length < 100)
                return "1000";

            return "MAX";
        }

        protected string _typename(SqlDbType type, object val)
        {
            if (type.In(SqlDbType.NChar, SqlDbType.NVarChar, SqlDbType.VarChar, SqlDbType.Char))
                return type.ToString().ToLower() + "(" + guessCharSizeNeeded(val) + ")";

            return type.ToString().ToLower();
        }

        protected string _fieldDefinitions(Dictionary<string, object> fields)
        {
            var nullFields = fields.WhereValues(o => o == null || o == DBNull.Value).Keys;
            var fieldTypes = fields.SelectVals(v => SqlTypeConversion.InferSqlType(v));
            var fieldTypeStrs = fields.Keys.MapIndex(f => _typename(fieldTypes[f], fields[f]));

            var fieldDefs = fieldTypeStrs.ToList(
                (f,type) => f + " " + type + " " + (nullFields.Contains(f) ? "NULL" : "NOT NULL"));

            return fieldDefs.Join(",");
        }
    }
}