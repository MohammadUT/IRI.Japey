﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace IRI.Jab.Common.Extensions
{
    public static class BoundingBoxExtensions
    {
        public static Rect AsRect(this Ham.SpatialBase.BoundingBox boundingBox)
        {
            return new Rect(boundingBox.BottomRight.AsWpfPoint(), boundingBox.TopLeft.AsWpfPoint());
        }
    }
}
