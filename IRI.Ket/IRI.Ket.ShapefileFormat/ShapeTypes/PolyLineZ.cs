﻿// besmellahe rahmane rahim
// Allahomma ajjel le-valiyek al-faraj

using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Runtime.Serialization;

namespace IRI.Ket.ShapefileFormat.EsriType
{


    public struct PolyLineZ : IPointsWithZ
    {

        /// <summary>
        /// MinX, MinY, MaxX, MaxY
        /// </summary>
        private IRI.Ham.SpatialBase.BoundingBox boundingBox;

        private EsriPoint[] points;

        public int NumberOfPoints
        {
            get { return this.points.Length; }
        }

        /// <summary>
        /// Points for All Parts
        /// </summary>
        public EsriPoint[] Points
        {
            get { return this.points; }
        }

        public int[] Parts
        {
            get { return this.parts; }
        }

        /// <summary>
        /// Index to First Point in Part
        /// </summary>
        private int[] parts;

        public int NumberOfParts
        {
            get { return this.parts.Length; }
        }


        private double minMeasure, maxMeasure;

        private double[] measures;

        public double MinMeasure
        {
            get { return this.minMeasure; }
        }

        public double MaxMeasure
        {
            get { return this.maxMeasure; }
        }

        public double[] Measures
        {
            get { return this.measures; }
        }


        private double minZ, maxZ;

        private double[] zValues;

        public double MinZ
        {
            get { return this.minZ; }
        }

        public double MaxZ
        {
            get { return this.maxZ; }
        }

        public double[] ZValues
        {
            get { return this.zValues; }
        }

        public PolyLineZ(EsriPoint[] points, int[] parts, double[] zValues, double[] measures)
        {
            if (points.Length != zValues.Length || points.Length != measures.Length)
            {
                throw new NotImplementedException();
            }

            this.boundingBox = IRI.Ham.SpatialBase.BoundingBox.CalculateBoundingBox(points.Cast<IRI.Ham.SpatialBase.IPoint>());

            this.parts = parts;

            this.points = points;

            this.measures = measures;

            this.zValues = zValues;

            if (measures?.Count() > 0)
            {
                this.minMeasure = measures.Min();

                this.maxMeasure = measures.Max();
            }
            else
            {
                this.minMeasure = ShapeConstants.NoDataValue;

                this.maxMeasure = ShapeConstants.NoDataValue;
            }

            if (zValues?.Count() > 0)
            {
                this.minZ = zValues.Min();

                this.maxZ = zValues.Max();
            }
            else
            {
                this.minZ = ShapeConstants.NoDataValue;

                this.maxZ = ShapeConstants.NoDataValue;
            }
              
        }

        public PolyLineZ(IRI.Ham.SpatialBase.BoundingBox boundingBox,
                            int[] parts,
                            EsriPoint[] points,
                            double minZ,
                            double maxZ,
                            double[] zValues,
                            double minMeasure,
                            double maxMeasure,
                            double[] measures)
        {
            this.boundingBox = boundingBox;

            this.parts = parts;

            this.points = points;

            this.minZ = minZ;

            this.maxZ = maxZ;

            this.zValues = zValues;

            this.minMeasure = minMeasure;

            this.maxMeasure = maxMeasure;

            this.measures = measures;
        }

        //private Point[] GetPoints(int partNumber)
        //{
        //    int startIndex = parts[partNumber];

        //    int lastIndex = (partNumber == NumberOfParts - 1) ? NumberOfPoints - 1 : parts[partNumber + 1] - 1;

        //    Point[] result = new Point[lastIndex - startIndex + 1];

        //    Array.ConstrainedCopy(this.Points, startIndex, result, 0, result.Length);

        //    return result;
        //}

        //private double[] GetMeasures(int partNumber)
        //{
        //    int startIndex = parts[partNumber];

        //    int lastIndex = (partNumber == NumberOfParts - 1) ? NumberOfPoints - 1 : parts[partNumber + 1] - 1;

        //    double[] result = new double[lastIndex - startIndex + 1];

        //    Array.ConstrainedCopy(this.Measures, startIndex, result, 0, result.Length);

        //    return result;
        //}

        //private double[] GetZValues(int partNumber)
        //{
        //    int startIndex = parts[partNumber];

        //    int lastIndex = (partNumber == NumberOfParts - 1) ? NumberOfPoints - 1 : parts[partNumber + 1] - 1;

        //    double[] result = new double[lastIndex - startIndex + 1];

        //    Array.ConstrainedCopy(this.ZValues, startIndex, result, 0, result.Length);

        //    return result;
        //}        

        public byte[] WriteContentsToByte()
        {
            System.IO.MemoryStream result = new System.IO.MemoryStream();

            result.Write(System.BitConverter.GetBytes((int)ShapeType.PolyLineZ), 0, ShapeConstants.IntegerSize);

            result.Write(Writer.ShpWriter.WriteBoundingBoxToByte(this), 0, 4 * ShapeConstants.DoubleSize);

            result.Write(System.BitConverter.GetBytes(this.NumberOfParts), 0, ShapeConstants.IntegerSize);

            result.Write(System.BitConverter.GetBytes(this.NumberOfPoints), 0, ShapeConstants.IntegerSize);

            foreach (int item in this.parts)
            {
                result.Write(System.BitConverter.GetBytes(item), 0, ShapeConstants.IntegerSize);
            }


            byte[] tempPoints = Writer.ShpWriter.WritePointsToByte(this.points);

            result.Write(tempPoints, 0, tempPoints.Length);


            byte[] tempZ = Writer.ShpWriter.WriteAdditionalData(this.MinZ, this.MaxZ, this.ZValues);

            result.Write(tempZ, 0, tempZ.Length);


            byte[] tempMeasures = Writer.ShpWriter.WriteAdditionalData(this.MinMeasure, this.MaxMeasure, this.Measures);

            result.Write(tempMeasures, 0, tempMeasures.Length);

            return result.ToArray();
        }

        public int ContentLength
        {
            get { return 22 + 2 * NumberOfParts + 8 * NumberOfPoints + 2 * (8 + 4 * NumberOfPoints); }
        }

        public ShapeType Type
        {
            get { return ShapeType.PolyLineZ; }
        }

        public EsriPoint[] GetPart(int partNo)
        {
            return ShapeHelper.GetPoints(this, Parts[partNo]);
        }


        public IRI.Ham.SpatialBase.BoundingBox MinimumBoundingBox
        {
            get { return boundingBox; }
        }

        public string AsSqlServerWkt()
        {
            StringBuilder result = new StringBuilder("MULTILINESTRING(");

            for (int i = 0; i < NumberOfParts; i++)
            {
                result.Append(
                    string.Format("{0},",
                    SqlServerWktMapFunctions.PointZGroupElementToWkt(
                        ShapeHelper.GetPoints(this, i), ShapeHelper.GetZValues(this, i), ShapeHelper.GetMeasures(this, this.Parts[i]))));
            }

            return result.Remove(result.Length - 1, 1).Append(")").ToString();
        }

        /// <summary>
        /// Changed but not tested. 93.03.21
        /// </summary>
        /// <returns></returns>
        public byte[] AsWkb()
        {
            List<byte> result = new List<byte>();


            if (this.Parts.Count() == 1)
            {
                result.AddRange(OgcWkbMapFunctions.ToWkbLineStringZM(
                    ShapeHelper.GetPoints(this, 0), ShapeHelper.GetZValues(this, 0), ShapeHelper.GetMeasures(this, 0)));
            }
            else
            {
                result.Add((byte)IRI.Standards.OGC.SFA.WkbByteOrder.WkbNdr);

                result.AddRange(BitConverter.GetBytes((uint)IRI.Standards.OGC.SFA.WkbGeometryType.MultiLineStringZM));

                result.AddRange(BitConverter.GetBytes((uint)this.parts.Length));

                for (int i = 0; i < this.parts.Length; i++)
                {
                    result.AddRange(
                        OgcWkbMapFunctions.ToWkbLineStringZM(
                            ShapeHelper.GetPoints(this, this.Parts[i]), ShapeHelper.GetZValues(this, this.Parts[i]), ShapeHelper.GetMeasures(this, this.Parts[i])));
                }
            }

            return result.ToArray();
        }


        public IRI.Ket.KmlFormat.Primitives.PlacemarkType AsPlacemark(Func<IRI.Ham.SpatialBase.Point, IRI.Ham.SpatialBase.Point> projectToGeodeticFunc = null, byte[] color = null)
        {
            return AsPlacemark(this, projectToGeodeticFunc);
        }

        /// <summary>
        /// Returs Kml representation of the point. Note: Z,M values are igonred
        /// </summary>
        /// <returns></returns>
        static IRI.Ket.KmlFormat.Primitives.PlacemarkType AsPlacemark(PolyLineZ polyline, Func<IRI.Ham.SpatialBase.Point, IRI.Ham.SpatialBase.Point> projectToGeodeticFunc = null, byte[] color = null)
        {
            IRI.Ket.KmlFormat.Primitives.PlacemarkType placemark =
               new KmlFormat.Primitives.PlacemarkType();

            List<IRI.Ket.KmlFormat.Primitives.LineStringType> linestrings =
                new List<KmlFormat.Primitives.LineStringType>();

            IRI.Ket.KmlFormat.Primitives.MultiGeometryType multiGeometry =
                new KmlFormat.Primitives.MultiGeometryType();

            IEnumerable<string> coordinates;

            if (projectToGeodeticFunc != null)
            {
                coordinates = polyline.parts
                    .Select(i =>
                        string.Join(" ", ShapeHelper.GetPoints(polyline, i)
                        .Select(j =>
                        {
                            var temp = projectToGeodeticFunc(new IRI.Ham.SpatialBase.Point(j.X, j.Y));
                            return string.Format("{0},{1}", temp.X, temp.Y);
                        }).ToArray()));
            }
            else
            {
                coordinates = polyline.Parts
                    .Select(i =>
                        string.Join(" ", ShapeHelper.GetPoints(polyline, i)
                        .Select(j => string.Format("{0},{1}", j.X, j.Y))
                        .ToArray()));
            }

            foreach (string item in coordinates)
            {
                IRI.Ket.KmlFormat.Primitives.LineStringType linestring = new KmlFormat.Primitives.LineStringType();

                linestring.coordinates = item;

                linestrings.Add(linestring);
            }

            multiGeometry.AbstractGeometryObjectExtensionGroup = linestrings.ToArray();

            placemark.AbstractFeatureObjectExtensionGroup = new KmlFormat.Primitives.AbstractObjectType[] { multiGeometry };

            return placemark;
        }

        public string AsKml(Func<IRI.Ham.SpatialBase.Point, IRI.Ham.SpatialBase.Point> projectToGeodeticFunc = null)
        {
            return OgcKmlMapFunctions.AsKml(this.AsPlacemark(projectToGeodeticFunc));
        }

        public IShape Transform(Func<Ham.SpatialBase.IPoint, Ham.SpatialBase.IPoint> transform)
        {
            return new PolyLineZ(this.Points.Select(i => i.Transform(transform)).Cast<EsriPoint>().ToArray(), this.Parts, this.ZValues, this.Measures);
        }
    }
}
