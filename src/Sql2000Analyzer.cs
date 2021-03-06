﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Lasy
{
    public class Sql2000Analyzer : SqlAnalyzer
    {
        public Sql2000Analyzer(string connectionString, TimeSpan cacheDuration = default(TimeSpan))
            : base(connectionString, new Sql2000NameQualifier(), cacheDuration)
        { }

        protected internal override string _getPrimaryKeySql()
        {
            return @"SELECT     
                isc.COLUMN_NAME as [Name]
            FROM 
                sysobjects tbl
                inner join syscolumns c
                on tbl.id = c.id
                inner join information_schema.columns isc
                on isc.column_name = c.name and isc.table_name = tbl.name
                left outer join information_schema.key_column_usage k
                on k.table_name = tbl.name and objectproperty(object_id(constraint_name), 'IsPrimaryKey') = 1
		            and k.column_name = c.name
            WHERE 
                tbl.xtype = 'U'
                and tbl.name = @table
                AND objectproperty(object_id(constraint_name), 'IsPrimaryKey') = 1
            order by isc.ORDINAL_POSITION";
        }

        protected internal override string _getAutonumberKeySql()
        {
            return @"SELECT     
                isc.COLUMN_NAME as [Name]
            FROM 
                sysobjects tbl
                inner join syscolumns c
                on tbl.id = c.id
                inner join information_schema.columns isc
                on isc.column_name = c.name and isc.table_name = tbl.name
            WHERE 
                tbl.xtype = 'U'
                and tbl.name = @table
                AND c.status & 0x80 = 0x80
            order by isc.ORDINAL_POSITION";
        }

        protected internal override string _getTableExistsSql(string schema, string table)
        {
            return @"SELECT 1 FROM sysobjects tbl 
                WHERE tbl.xtype = 'U' and tbl.name = @table";
        }

        protected internal override string _getSchemaExistsSql()
        {
            // Do nothing - SQL 2000 doesn't support schemas
            // The only schema is dbo
            return "select @schema = 'dbo'";
        }
    }
}
