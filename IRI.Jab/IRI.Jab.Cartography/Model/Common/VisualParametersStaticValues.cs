﻿using IRI.Jab.Cartography.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Media;

namespace IRI.Jab.Cartography
{
    public partial class VisualParameters
    {



        public static VisualParameters GetFill(Color fill, double opacity = 1)
        {
            return new VisualParameters(new SolidColorBrush(fill), null, 0, opacity);
        }

        public static VisualParameters GetStroke(Color stroke, double strokeThickness = 1, double opacity = 1)
        {
            return new VisualParameters(null, new SolidColorBrush(stroke), strokeThickness, opacity);
        }

        public static VisualParameters Get(Color fill, Color stroke, double strokeThickness, double opacity = 1)
        {
            return new VisualParameters(new SolidColorBrush(fill), new SolidColorBrush(stroke), strokeThickness, opacity);
        }


        public static VisualParameters GetDefaultForDrawing(DrawMode mode)
        {
            var result = new VisualParameters(mode == DrawMode.Polygon ? DefaultDrawingFill : null, DefaultDrawingStroke, 2, .7);

            return result;
        }

        public static SolidColorBrush DefaultDrawingStroke = new SolidColorBrush(new Color() { R = 255, G = 200, B = 0, A = 250 });

        public static SolidColorBrush DefaultDrawingFill = new SolidColorBrush(new Color() { R = 255, G = 200, B = 0, A = 160 });

      
    }
}
