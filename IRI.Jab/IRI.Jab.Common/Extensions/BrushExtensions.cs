﻿using IRI.Ham.Common.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Media;

namespace IRI.Jab.Common.Extensions
{
    public static class BrushExtensions
    {

        public static System.Windows.Media.Color? AsSolidColor(this Brush brush)
        {
            var solidBrush = brush as SolidColorBrush;

            return solidBrush != null ? solidBrush.Color : (Color?)null;
        }

        public static System.Drawing.Color? AsGdiSolidColor(this Brush brush)
        {
            var color = brush.AsSolidColor();

            return color.HasValue ? color.Value.AsGdiColor() : (System.Drawing.Color?)null;
        }

        public static System.Drawing.Brush AsGdiBrush(this Brush brush)
        {
            var solidColorBrush = brush as SolidColorBrush;

            return solidColorBrush != null ? new System.Drawing.SolidBrush(solidColorBrush.Color.AsGdiColor()) : null;
        }


    }
}
