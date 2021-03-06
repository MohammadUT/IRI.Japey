﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.SqlServer.Types;
using IRI.Ham.SpatialBase;
using System.Data;
using IRI.Ket.DataManagement.Model;
using IRI.Ket.SqlServerSpatialExtension.Model;
using IRI.Ket.SpatialExtensions;

namespace IRI.Ket.DataManagement.DataSource
{
    public class MemoryDataSource<T> : IFeatureDataSource
    {
        public override BoundingBox Extent { get; protected set; }

        protected List<SqlGeometry> _geometries;

        protected List<T> _attributes;

        protected Func<T, string> _labelFunc;

        protected List<NamedSqlGeometry> geometryAttributePairs;

        private int Srid { get { return this._geometries == null || this._geometries.Count == 0 ? 0 : this._geometries.First().GetSrid(); } }

        public static MemoryDataSource<object> Create(SqlGeometry geometry)
        {
            return new MemoryDataSource<object>(new List<SqlGeometry>() { geometry });
        }

        public MemoryDataSource()
        {

        }

        public MemoryDataSource(List<SqlGeometry> geometries)
            : this(geometries, null)
        {
        }

        public MemoryDataSource(List<SqlGeometry> geometries, List<T> attributes)
            : this(geometries, attributes, null)
        { }

        public MemoryDataSource(List<SqlGeometry> geometries, List<T> attributes, Func<T, string> labelFunc)
        {
            if (attributes == null || labelFunc == null)
            {
                this._geometries = geometries;
            }
            else
            {
                //this.geometryAttributePairs = geometries.Zip(attributes, (a, b) =>  Tuple.Create(a, labelFunc(b))).ToList();
                this.geometryAttributePairs = geometries.Zip(attributes, (a, b) => new NamedSqlGeometry(a, labelFunc(b))).ToList();
            }
            //this._geometries = geometries;

            this._attributes = attributes;

            this._labelFunc = labelFunc;

            this.Extent = geometries.GetBoundingBox();
        }

        public override List<SqlGeometry> GetGeometries()
        {
            if (this._geometries == null)
            {
                return this.geometryAttributePairs.Select(i => i.Geometry).ToList();
            }
            else
            {
                return this._geometries;
            }
        }

        public override List<SqlGeometry> GetGeometries(string whereClause)
        {
            throw new NotImplementedException();
        }

        public override List<SqlGeometry> GetGeometries(BoundingBox boundingBox)
        {
            //SqlGeometry boundary =
            //     SqlGeometry.Parse(
            //         string.Format("POLYGON(({0} {1}, {0} {2}, {3} {2}, {3} {1}, {0} {1}))", boundingBox.XMin, boundingBox.YMin, boundingBox.YMax, boundingBox.XMax));

            //return GetGeometries().Where(i => i.STWithin(boundary).Value).ToList();
            SqlGeometry boundary = boundingBox.AsSqlGeometry(this.Srid).MakeValid();

            return GetGeometries().Where(i => i.STIntersects(boundary).IsTrue).ToList();
        }

        //public virtual Task<List<SqlGeometry>> GetGeometriesAsync(BoundingBox boundingBox)
        //{
        //    return Task.Run<List<SqlGeometry>>(() => { return GetGeometries(boundingBox); });
        //}

        public List<object> GetAttributes()
        {
            return this._attributes.Cast<object>().ToList();
        }

        //public override List<object> GetAttributes(string attributeColumn)
        //{
        //    throw new NotImplementedException();
        //}

        public override List<object> GetAttributes(string attributeColumn, string whereClause)
        {
            throw new NotImplementedException();
        }

        //public System.Data.DataTable ExecuteSql(string commandString)
        //{
        //    throw new NotImplementedException();
        //}

        //public System.Data.DataTable GetEntireFeature()
        //{
        //    throw new NotImplementedException();
        //}
        //public Task<DataTable> GetEntireFeatureAsync(string whereClause)
        //{
        //    throw new NotImplementedException();
        //}

        public override DataTable GetEntireFeature(string whereClause)
        {
            throw new NotImplementedException();
        }

        //public List<string> GetLabels()
        //{
        //    //return this._attributes.Select(i => i.ToString()).ToList();
        //    return this.geometryAttributePairs.Select(i => i.Item2.ToString()).ToList();
        //}

        //public IEnumerable<Tuple<SqlGeometry, string>> GetGeometryLabelPairs()
        //{
        //    //return _geometries.Zip(_attributes, (a, b) => Tuple.Create(a, _labelFunc(b))).ToList();
        //    return this.geometryAttributePairs;
        //}

        //public IEnumerable<Tuple<SqlGeometry, string>> GetGeometryLabelPairs(string whereClause)
        //{
        //    throw new NotImplementedException();
        //}

        public override List<NamedSqlGeometry> GetGeometryLabelPairs(BoundingBox boundingBox)
        {
            //SqlGeometry boundary =
            //    SqlGeometry.Parse(
            //        string.Format("POLYGON(({0} {1}, {0} {2}, {3} {2}, {3} {1}, {0} {1}))", boundingBox.XMin, boundingBox.YMin, boundingBox.YMax, boundingBox.XMax));

            //return _geometries.Zip(_attributes, (a, b) => Tuple.Create(a, _labelFunc(b))).Where(i => i.Item1.STWithin(boundary).Value).ToList();
            SqlGeometry boundary = boundingBox.AsSqlGeometry();

            //93.01.18
            //return _geometries.Zip(_attributes, (a, b) => Tuple.Create(a, _labelFunc(b))).Where(i => i.Item1.STIntersects(boundary).Value).ToList();

            return this.geometryAttributePairs.Where(i => i.Geometry.STIntersects(boundary).Value).ToList();
        }

        public override DataTable GetEntireFeaturesWhereIntersects(SqlGeometry geometry)
        {
            throw new NotImplementedException();
        }

        //public Task<IEnumerable<NamedSqlGeometry>> GetGeometryLabelPairsAsync(BoundingBox boundingBox)
        //{
        //    return Task.Run(() => { return GetGeometryLabelPairs(boundingBox); });
        //}




    }
}
