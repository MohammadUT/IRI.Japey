﻿// besmellahe rahmane rahim
// Allahomma ajjel le-valiyek al-faraj

using IRI.Ham.SpatialBase;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace IRI.Ket.ShapefileFormat.EsriType
{


    public struct EsriPointM : IRI.Ham.SpatialBase.IPoint, IShape
    {
        private double x, y, measure;

        public double X
        {
            get { return this.x; }
            set { this.x = value; }
        }

        public double Y
        {
            get { return this.y; }
            set { this.y = value; }
        }

        public double Measure
        {
            get { return this.measure; }
        }

        public EsriPointM(double x, double y, double measure)
        {
            this.x = x;

            this.y = y;

            this.measure = measure;
        }


        public IRI.Ham.SpatialBase.BoundingBox MinimumBoundingBox
        {
            get { return new IRI.Ham.SpatialBase.BoundingBox(this.X, this.Y, this.X, this.Y); }
        }

        //public byte[] WriteContentsToByte()
        //{
        //    System.IO.MemoryStream result = new System.IO.MemoryStream();

        //    result.Write(System.BitConverter.GetBytes((int)ShapeType.PointM), 0, ShapeConstants.IntegerSize);

        //    result.Write(System.BitConverter.GetBytes(this.X), 0, ShapeConstants.DoubleSize);

        //    result.Write(System.BitConverter.GetBytes(this.Y), 0, ShapeConstants.DoubleSize);

        //    result.Write(System.BitConverter.GetBytes(this.Measure), 0, ShapeConstants.DoubleSize);

        //    return result.ToArray();
        //}

        public byte[] WriteContentsToByte()
        {
            System.IO.MemoryStream result = new System.IO.MemoryStream();

            result.Write(System.BitConverter.GetBytes((int)ShapeType.PointM), 0, ShapeConstants.IntegerSize);

            result.Write(Writer.ShpWriter.CheckNoDataAndGetByteValue(this.X), 0, ShapeConstants.DoubleSize);

            result.Write(Writer.ShpWriter.CheckNoDataAndGetByteValue(this.Y), 0, ShapeConstants.DoubleSize);

            result.Write(Writer.ShpWriter.CheckNoDataAndGetByteValue(this.Measure), 0, ShapeConstants.DoubleSize);

            return result.ToArray();
        }

        public int ContentLength
        {
            get { return ShapeConstants.PointMContentLengthInWords; }
        }

        public ShapeType Type
        {
            get { return ShapeType.PointM; }
        }

        public string AsSqlServerWkt()
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "POINT({0:G17} {1:G17} NULL {2:G17})", this.X, this.Y, this.Measure);
        }

        public byte[] AsWkb()
        {
            return OgcWkbMapFunctions.ToWkbPoint(this);
        }

        /// <summary>
        /// Returs Kml representation of the point. Note: Point must be in Lat/Long System
        /// </summary>
        /// <returns></returns>
        public IRI.Ket.KmlFormat.Primitives.PlacemarkType AsPlacemark(Func<IRI.Ham.SpatialBase.Point, IRI.Ham.SpatialBase.Point> projectFunc = null, byte[] color = null)
        {
            throw new NotImplementedException();
        }

        public string AsKml(Func<IRI.Ham.SpatialBase.Point, IRI.Ham.SpatialBase.Point> projectToGeodeticFunc = null)
        {
            return OgcKmlMapFunctions.AsKml(this.AsPlacemark(projectToGeodeticFunc));
        }

        public string AsExactString()
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:G17} {1:G17} {2:G17}", this.X, this.Y, this.Measure);
        }

        public IShape Transform(Func<Ham.SpatialBase.IPoint, Ham.SpatialBase.IPoint> transform)
        {
            var result = transform(this);

            return new EsriPointM(result.X, result.Y, this.Measure);
        }

        public bool AreExactlyTheSame(object obj)
        {
            if (obj.GetType() != typeof(EsriPointM))
            {
                return false;
            }

            return this.AsExactString() == ((EsriPointM)obj).AsExactString();
        }
         
        public double DistanceTo(IPoint point)
        {
            return Point.GetDistance(new Point(this.X, this.Y), new Point(point.X, point.Y));
        }

    }

}
