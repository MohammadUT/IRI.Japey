﻿// besmellahe rahmane rahim
// Allahomma ajjel le-valiyek al-faraj

using IRI.Ket.ShapefileFormat.EsriType;
using IRI.Ket.ShapefileFormat.ShpReader;
using System;
using System.Collections.Generic;
using System.Text;


namespace IRI.Ket.ShapefileFormat.Reader
{
    public class PolygonZReader : zReader<PolygonZ>
    {
        public PolygonZReader(string fileName)
            : base(fileName, ShapeType.PolygonZ)
        {

        }

        protected override PolygonZ ReadElement()
        {
            int shapeType = shpReader.ReadInt32();

            if ((ShapeType)shapeType != ShapeType.PolygonZ)
            {
                throw new NotImplementedException();
            }

            IRI.Ham.SpatialBase.BoundingBox boundingBox = this.ReadBoundingBox();

            int numParts = shpReader.ReadInt32();

            int numPoints = shpReader.ReadInt32();

            int[] parts = new int[numParts];

            for (int i = 0; i < numParts; i++)
            {
                parts[i] = shpReader.ReadInt32();
            }

            EsriPoint[] points = this.ReadPoints(numPoints);

            double minZ, maxZ;

            double[] zValues;

            this.ReadZValues(numPoints, out minZ, out maxZ, out zValues);

            double minMeasure = ShapeConstants.NoDataValue, maxMeasure = ShapeConstants.NoDataValue;

            double[] measures = new double[numPoints];

            if (shpReader.BaseStream.Position != shpReader.BaseStream.Length)
            {
                this.ReadMeasures(numPoints, out minMeasure, out maxMeasure, out measures);
            }

            return new PolygonZ(boundingBox, parts, points, minZ, maxZ, zValues, minMeasure, maxMeasure, measures);

        }

        public static PolygonZ Read(System.IO.BinaryReader reader, int offset, int contentLength)
        {
            //+8: pass the record header; +4 pass the shapeType
            reader.BaseStream.Position = offset * 2 + 8 + 4;

            //var byteArray = reader.ReadBytes(contentLength * 2 - 8);

            var boundingBox = ShpBinaryReader.ReadBoundingBox(reader);

            int numParts = reader.ReadInt32();

            int numPoints = reader.ReadInt32();

            int[] parts = new int[numParts];

            for (int i = 0; i < numParts; i++)
            {
                parts[i] = reader.ReadInt32();
            }

            var points = ShpBinaryReader.ReadPoints(reader, numPoints);
             
            double minZ, maxZ;

            double[] zValues;

            ShpBinaryReader.ReadValues(reader, numPoints, out minZ, out maxZ, out zValues);

            double minMeasure = ShapeConstants.NoDataValue, maxMeasure = ShapeConstants.NoDataValue;

            double[] measures = new double[numPoints];

            if (contentLength > reader.BaseStream.Position * 2 - offset)
            {
                ShpBinaryReader.ReadValues(reader, numPoints, out minMeasure, out maxMeasure, out measures);
            }

            return new PolygonZ(boundingBox, parts, points, minZ, maxZ, zValues, minMeasure, maxMeasure, measures);
        }
    }
}
