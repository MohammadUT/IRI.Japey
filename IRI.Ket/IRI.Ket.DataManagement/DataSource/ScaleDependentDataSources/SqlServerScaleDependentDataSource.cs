﻿using IRI.Ket.DataManagement.Model;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IRI.Ham.SpatialBase;
using Microsoft.SqlServer.Types;
using System.Data;
using System.Globalization;
using IRI.Ket.SpatialExtensions;
using System.Linq;
using IRI.Ket.SqlServerSpatialExtension.Model;

namespace IRI.Ket.DataManagement.DataSource.ScaleDependentDataSources
{
    public class SqlServerScaleDependentDataSource : SqlServerDataSource, IScaleDependentDataSource
    {
        SqlServerSourceParameter _pyramidParameters;

        List<int> _levels;

        string _schema;

        const string _pyramidTableSpatialColumn = "GEO";

        const string _defaultSchema = "GIS";

        protected SqlServerScaleDependentDataSource()
        {

        }

        public SqlServerScaleDependentDataSource(SqlServerSourceParameter sourceParameters, SqlServerSourceParameter pyramidParameters, bool createPyramid = false, string schema = _defaultSchema)
        {
            this._connectionString = sourceParameters.ConnectionString;

            this._tableName = sourceParameters.TableName;

            this._spatialColumnName = sourceParameters.SpatialColumnName;

            this._labelColumnName = sourceParameters.LabelColumn;

            this._queryString = sourceParameters.QueryString;

            this._pyramidParameters = pyramidParameters;

            this._schema = string.IsNullOrEmpty(schema) ? "GIS" : schema;

            if (sourceParameters.SpatialColumnName == null)
            {
                this.Extent = BoundingBox.NaN;
            }
            else
            {
                //IMPORTANT!
                //this.Extent = GetGeometries().GetBoundingBox();
            }

            if (createPyramid)
            {
                Initialize();
            }

            _levels = GetLevels();

        }

        private void Initialize()
        {
            var command = $"CREATE TABLE GIS.{_pyramidParameters.TableName} ({_pyramidTableSpatialColumn} GEOMETRY NOT NULL, MBB GEOMETRY NOT NULL, Level INT NOT NULL)";

            if (Infrastructure.SqlServerInfrastructure.Exists(_pyramidParameters.TableName, _pyramidParameters.ConnectionString))
                Infrastructure.SqlServerInfrastructure.Delete(_pyramidParameters.TableName, _pyramidParameters.ConnectionString);

            Infrastructure.SqlServerInfrastructure.ExecuteNonQuery(command, this._pyramidParameters.ConnectionString);

            //var originalGeometries = GetGeometries();

            //for (int i = 1; i < 11; i++)
            //{
            //    var geometries = originalGeometries.Simplify(SqlServerSpatialExtension.Analysis.SimplificationType.AdditiveByAreaAngle, i);

            //    geometries = geometries.Simplify(SqlServerSpatialExtension.Analysis.SimplificationType.AdditiveByArea, i);

            //    CopyToSqlServer(_pyramidParameters.TableName, geometries, i);
            //}
            var originalGeometries = GetGeometries();

            for (int i = 15; i > 1; i--)
            {
                System.Diagnostics.Debug.WriteLine(string.Empty);

                var geometries = originalGeometries.Simplify(SqlServerSpatialExtension.Analysis.SimplificationType.AdditiveByAreaAngle, i);

                geometries = geometries.Simplify(SqlServerSpatialExtension.Analysis.SimplificationType.AdditiveByArea, i);

                CopyToSqlServer(_pyramidParameters.TableName, geometries, i - 1);
            }
        }

        private void CopyToSqlServer(string tableName, List<SqlGeometry> geometries, int level)
        {
            var infra = new Infrastructure.SqlServerInfrastructure();

            DataTable table = new DataTable();

            table.Columns.Add(GetSpatialColumn("Geo"));

            table.Columns.Add(GetSpatialColumn("MBB"));

            table.Columns.Add(new DataColumn("Level", typeof(int)) { AllowDBNull = false });

            for (int i = 0; i < geometries.Count; i++)
            {
                var row = table.NewRow();

                row["Level"] = level;

                row["Geo"] = geometries[i];

                row["MBB"] = geometries[i].STEnvelope();

                table.Rows.Add(row);
            }

            infra.InsertTable(this._pyramidParameters.ConnectionString, table, tableName, false);
        }

        private DataColumn GetSpatialColumn(string name)
        {
            DataColumn spatialColumn = new DataColumn(name, typeof(SqlGeometry));

            spatialColumn.AllowDBNull = false;

            return spatialColumn;
        }

        public List<SqlGeometry> GetGeometries(double mapScale)
        {
            return GetGeometries(mapScale, string.Empty);
        }

        public List<SqlGeometry> GetGeometries(double mapScale, BoundingBox boundingBox)
        {
            int srid = GetSrid();

            //var whereClause = $" (GEO.STIntersects(GEOMETRY::STPolyFromText('{boundingBox.AsWkt()}',{srid})) = 1) ";
            var whereClause = GetWhereClause(_pyramidTableSpatialColumn, boundingBox, srid);

            return GetGeometries(mapScale, whereClause);
        }

        public List<SqlGeometry> GetGeometries(double mapScale, string whereClause)
        {
            int zoomLevel = IRI.Ham.SpatialBase.Mapping.WebMercatorUtility.GetZoomLevel(mapScale, 35);

            if (zoomLevel > _levels.Max())
            {
                zoomLevel = _levels.Max();
            }

            if (string.IsNullOrWhiteSpace(whereClause))
            {
                whereClause = $" Level = {zoomLevel} ";
            }
            else
            {
                whereClause = $" (Level = {zoomLevel} ) AND {whereClause} ";
            }

            //return SelectGeometries(
            //    string.Format(
            //        CultureInfo.InvariantCulture,
            //        "SELECT GEO FROM {0} {1} ", _pyramidParameters.TableName, whereClause ?? string.Empty),
            //    _pyramidParameters.ConnectionString);

            return SelectGeometries(FormattableString.Invariant($"SELECT {_pyramidTableSpatialColumn} FROM {_pyramidParameters.TableName} {MakeWhereClause(whereClause)} "), _pyramidParameters.ConnectionString);
        }




        public List<NamedSqlGeometry> GetGeometryLabelPairs(double mapScale, BoundingBox boundingBox)
        {
            int srid = GetSrid();

            //var whereClause = $" (GEO.STIntersects(GEOMETRY::STPolyFromText('{boundingBox.AsWkt()}',{srid})) = 1) ";
            var whereClause = GetWhereClause(_pyramidTableSpatialColumn, boundingBox, srid);

            int zoomLevel = IRI.Ham.SpatialBase.Mapping.WebMercatorUtility.GetZoomLevel(mapScale, 35);

            if (!(_levels?.Count < 1) && zoomLevel > _levels.Max())
            {
                //zoomLevel = _levels.Max();

                return GetGeometryLabelPairs(boundingBox);
            }
            else
            {
                return GetGeometryLabelPairs(mapScale, whereClause);
            }
        }

        public List<NamedSqlGeometry> GetGeometryLabelPairs(double mapScale, string whereClause)
        {
            int zoomLevel = IRI.Ham.SpatialBase.Mapping.WebMercatorUtility.GetZoomLevel(mapScale, 35);

            if (zoomLevel > _levels.Max())
            {
                //zoomLevel = _levels.Max();

                return GetGeometryLabelPairs(whereClause);
            }

            if (string.IsNullOrWhiteSpace(whereClause))
            {
                whereClause = $" (Level = {zoomLevel}) ";
            }
            else
            {
                whereClause = $" (Level = {zoomLevel} ) AND {whereClause} ";
            }

            //return SelectGeometries(
            //    string.Format(
            //        CultureInfo.InvariantCulture,
            //        "SELECT GEO FROM {0} {1} ", _pyramidParameters.TableName, whereClause ?? string.Empty),
            //    _pyramidParameters.ConnectionString);

            //return SelectGeometries(FormattableString.Invariant($"SELECT GEO FROM {_pyramidParameters.TableName} {whereClause ?? string.Empty} "), _pyramidParameters.ConnectionString);

            //return GetGeometryLabelPairs(whereClause);
            //throw new Exception("label column not available in _P table");
            return SelectGeometryLabelPairs(FormattableString.Invariant($"SELECT {_pyramidTableSpatialColumn} FROM {_pyramidParameters.TableName} {MakeWhereClause(whereClause)} "), _pyramidParameters.ConnectionString);
        }

        public Task<List<SqlGeometry>> GetGeometriesAsync(double mapScale, BoundingBox boundingBox)
        {
            return Task.Run(() => { return GetGeometries(mapScale, boundingBox); });
        }

        public List<int> GetLevels()
        {
            return Select<int>(FormattableString.Invariant($"SELECT DISTINCT(Level) FROM {_pyramidParameters.TableName} "), _pyramidParameters.ConnectionString);
        }
    }
}