﻿using System;
using System.Collections.Generic;
using System.Linq;
using IRI.Jab.Common;
using System.Windows.Shapes;
using System.Windows.Media;
using IRI.Jab.Common.Extensions;
using IRI.Ham.CoordinateSystem;
using IRI.Ham.SpatialBase;
using System.Windows;
using System.Windows.Input;
using IRI.Jab.Cartography.Model.DataStructure;
using WpfPoint = System.Windows.Point;
using WpfGeometry = System.Windows.Media.Geometry;
using Geometry = IRI.Ham.SpatialBase.Primitives.Geometry;
using Point = IRI.Ham.SpatialBase.Point;
using LineSegment = System.Windows.Media.LineSegment;
using IRI.Ham.SpatialBase.Primitives;
using IRI.Jab.Common.Model;
using IRI.Ham.CoordinateSystem.MapProjection;
using IRI.Ket.SpatialExtensions;
using System.Collections.ObjectModel;
using IRI.Ket.Common.Helpers;
using IRI.Jab.Common.Assets.Commands;
using IRI.Jab.Common.Model.MapMarkers;

namespace IRI.Jab.Cartography
{
    public class EditableFeatureLayer : BaseLayer
    {
        //static readonly Brush _stroke = BrushHelper.FromHex("#FF1CA1E2");
        //static readonly Brush _fill = BrushHelper.FromHex("#661CA1E2");
        string _delete = "حذف";
        string _copy = "کپی";
        string _displayCoordinates = "نمایش مختصات";


        private Model.EditableFeatureLayerOptions _options;

        public Model.EditableFeatureLayerOptions Options { get => _options; }

        private Geometry _mercatorGeometry;

        private Path _feature;

        private PathGeometry _pathGeometry;

        RecursiveCollection<Locateable> _vertices;

        RecursiveCollection<Locateable> _midVertices;


        private SpecialPointLayer _primaryVerticesLayer;

        private SpecialPointLayer _midVerticesLayer;

        // Edge Length
        private SpecialPointLayer _edgeLabelLayer;

        // Vertext Coordinates 
        private SpecialPointLayer _primaryVerticesLabelLayer;


        Transform _toScreen;

        Func<double, double> _screenToMap;

        public event EventHandler OnRequestFinishDrawing;

        public event EventHandler OnRequestDeleteGeometry;

        public Action<FrameworkElement, MouseButtonEventArgs, ILocateable> RequestRightClickOptions;

        public Action RequestRemoveRightClickOptions;

        public Action<EditableFeatureLayer> RequestRefresh;

        public Action<Geometry> RequestFinishEditing;

        public Action<EditableFeatureLayer> RequestCancelEditing;

        public Action<EditableFeatureLayer> RquestShowCoordinates;

        public Action RequestCancelDrawing;

        public Action<Locateable> RequestSelectedLocatableChanged;

        public Action<Point> RequestZoomToPoint;

        public override BoundingBox Extent
        {
            get
            {
                return _mercatorGeometry.GetBoundingBox();
            }

            protected set
            {
                throw new NotImplementedException();
            }
        }

        public override RenderingApproach Rendering
        {
            get
            {
                return RenderingApproach.Default;
            }

            protected set
            {
                throw new NotImplementedException();
            }
        }

        public override LayerType Type
        {
            get
            {
                return LayerType.EditableItem;
            }

            protected set
            {
                throw new NotImplementedException();
            }
        }

        private WpfPoint ToScreen(WpfPoint point)
        {
            return _toScreen.Transform(point);
        }


        /// <summary>
        /// For Polygons do not repeat first point in the last point
        /// </summary>
        /// <param name="name"></param>
        /// <param name="mercatorPoints"></param>
        /// <param name="isClosed"></param>
        public EditableFeatureLayer(string name, List<Point> mercatorPoints, Transform toScreen, Func<double, double> screenToMap, GeometryType type, Model.EditableFeatureLayerOptions options = null)
            : this(name, Geometry.Create(mercatorPoints.Cast<IPoint>().ToArray(), type), toScreen, screenToMap, options)
        {

        }

        /// <summary>
        /// For Polygons do not repeat first point in the last point
        /// </summary>
        /// <param name="name"></param>
        /// <param name="mercatorPoints"></param>
        /// <param name="isClosed"></param>
        public EditableFeatureLayer(string name, Geometry mercatorGeometry, Transform toScreen, Func<double, double> screenToMap, Model.EditableFeatureLayerOptions options = null)
        {
            //this._isNewDrawingMode = isNewDrawing;
            this._options = options ?? Model.EditableFeatureLayerOptions.CreateDefault();

            this._options.RequestHandleIsEdgeLabelVisibleChanged = () => { UpdateEdgeLables(); };

            this.LayerName = name;

            this._mercatorGeometry = mercatorGeometry;

            this.Id = Guid.NewGuid();

            this._toScreen = toScreen;

            this._screenToMap = screenToMap;

            this.VisibleRange = ScaleInterval.All;

            //this.VisualParameters = new VisualParameters(_mercatorGeometry.IsRingBase() ? _fill : null, _stroke, 3, 1);
            this.VisualParameters = Options.Visual;


            this._feature = GetDefaultEditingPath();

            this._pathGeometry = new PathGeometry();

            MakePathGeometry();

            this._feature.Data = this._pathGeometry;

            //if 
            //{
            //    this._feature.MouseUp += (sender, e) => { this.RegisterMapOptionsForNewPath(e); };
            //}
            if (!Options.IsNewDrawing)
            {
                this._feature.MouseRightButtonDown += (sender, e) => { this.RegisterMapOptionsForEditPath(e); };
            }

            var layerType = Options.IsNewDrawing ? LayerType.EditableItem : LayerType.MoveableItem | LayerType.EditableItem;

            this._primaryVerticesLayer = new SpecialPointLayer("vert", new List<Locateable>(), 1, ScaleInterval.All, layerType) { AlwaysTop = true };

            this._primaryVerticesLayer.RequestSelectedLocatableChanged = (l) => this.RequestSelectedLocatableChanged?.Invoke(l);

            this._midVerticesLayer = new SpecialPointLayer("int. vert", new List<Locateable>(), .7, ScaleInterval.All, layerType) { AlwaysTop = true };

            this._edgeLabelLayer = new SpecialPointLayer("edge length", new List<Locateable>(), .9, ScaleInterval.All, layerType) { AlwaysTop = false };

            this._primaryVerticesLabelLayer = new SpecialPointLayer("vert length", new List<Locateable>(), .9, ScaleInterval.All, layerType) { AlwaysTop = false };

            ReconstructLocateables();

            if (Options.IsNewDrawing)
            {
                this.AddSemiVertex((Point)(mercatorGeometry.Points == null ? mercatorGeometry.Geometries.Last().Points.Last() : mercatorGeometry.Points.Last()));
            }
        }

        internal void StartNewPart(Point webMercatorPoint)
        {
            this._mercatorGeometry.Geometries.Last().AddLastPoint(webMercatorPoint);
            //this.AddVertex(webMercatorPoint);
            MakePathGeometry();

            ReconstructLocateables();
        }

        internal void CancelDrawing()
        {
            this.RequestCancelDrawing?.Invoke();
        }

        internal void FinishDrawingPart()
        {
            this._mercatorGeometry.AddNewPart();

            MakePathGeometry();

            ReconstructLocateables();

            //this._pathGeometry.Figures.Last().Segments.RemoveAt(this._pathGeometry.Figures.Last().Segments.Count - 1);

            //this._pathGeometry.Figures.Add(new PathFigure());
        }

        #region Private Methods

        private Path GetDefaultEditingPath()
        {
            var result = new Path()
            {
                Stroke = this.Options.Visual.Stroke,
                StrokeThickness = this.Options.Visual.StrokeThickness,
                StrokeDashArray = Options.Visual.DashStyle?.Dashes,
                Opacity = this.Options.Visual.Opacity,
                StrokeLineJoin = PenLineJoin.Round,
            };

            if (_mercatorGeometry.IsRingBase())
            {
                result.Fill = this.Options.Visual.Fill;
            }

            return result;
        }

        private void ReconstructLocateables()
        {
            this._vertices = new RecursiveCollection<Locateable>();

            this._midVertices = new RecursiveCollection<Locateable>();

            MakeLocateables(this._mercatorGeometry, _vertices, _midVertices);

            this._primaryVerticesLayer.Items.Clear();

            this._midVerticesLayer.Items.Clear();

            this._primaryVerticesLabelLayer.Items.Clear();

            var primary = _vertices.GetFlattenCollection();

            var mid = _midVertices.GetFlattenCollection();

            foreach (var item in mid)
            {
                _midVerticesLayer.Items.Add(item);
            }

            foreach (var item in primary)
            {
                _primaryVerticesLayer.Items.Add(item);
            }

            UpdateEdgeLables();
        }

        private void UpdateEdgeLables()
        {
            this._edgeLabelLayer.Items.Clear();

            if (Options.IsEdgeLabelVisible)
            {
                var edges = _mercatorGeometry.GetLineSegments().Select(i => ToEdgeLengthLocatable(i.Start, i.End));

                foreach (var item in edges)
                {
                    _edgeLabelLayer.Items.Add(item);
                }

                var point = this._mercatorGeometry?.GetMeanOrLastPoint();

                if (point != null && Options.IsMeasureVisible)
                {
                    var element = new Common.View.MapMarkers.RectangleLabelMarker(MeasureLabel);

                    var offset = _screenToMap(20);

                    _edgeLabelLayer.Items.Add(new Locateable(Model.AncherFunctionHandlers.BottomCenter) { Element = element, X = point.X + offset, Y = point.Y + offset });

                }
            }

            //if (Options.IsAutoMeasureEnabled && _mercatorGeometry.IsRingBase())
            //{
            //    RaisePropertyChanged(nameof(AreaLabel));
            //}
        }

        private void UpdateCoordinate(Locateable locatable)
        {
            var locatables = this._primaryVerticesLabelLayer.Get(locatable.Id);

            foreach (var item in locatables)
            {
                (item.Element as Common.View.MapMarkers.CoordinateMarker).MercatorLocation = new Point(locatable.X, locatable.Y);

                item.X = locatable.X;
                item.Y = locatable.Y;
            }
        }

        private void UpdatePrimaryVerticesLabels()
        {
            this._primaryVerticesLabelLayer.Items.Clear();


        }

        private void MakeLocateables(Geometry geometry, RecursiveCollection<Locateable> primaryCollection, RecursiveCollection<Locateable> midCollection)
        {
            if (geometry.Points != null)
            {
                primaryCollection.Values = new List<Locateable>();

                midCollection.Values = new List<Locateable>();

                for (int i = 0; i < geometry.Points.Length; i++)
                {
                    var locateable = ToPrimaryLocateable(geometry.Points[i]);

                    primaryCollection.Values.Add(locateable);

                    //do not make mid points in drawing mode
                    if (Options.IsNewDrawing)
                        continue;

                    if (geometry.Type == GeometryType.Point || geometry.Type == GeometryType.MultiPoint)
                        continue;

                    if (i == geometry.Points.Length - 1)
                    {
                        if (_mercatorGeometry.IsRingBase())
                        {
                            midCollection.Values.Add(ToSecondaryLocateable(geometry.Points[i], geometry.Points[0]));
                        }
                    }
                    else
                    {
                        midCollection.Values.Add(ToSecondaryLocateable(geometry.Points[i], geometry.Points[i + 1]));
                    }
                }
            }
            else
            {
                primaryCollection.Collections = new List<RecursiveCollection<Locateable>>();

                midCollection.Collections = new List<RecursiveCollection<Locateable>>();

                foreach (var item in geometry.Geometries)
                {
                    RecursiveCollection<Locateable> subPrimaryCollection = new RecursiveCollection<Locateable>();

                    RecursiveCollection<Locateable> subMidCollection = new RecursiveCollection<Locateable>();

                    MakeLocateables(item, subPrimaryCollection, subMidCollection);

                    primaryCollection.Collections.Add(subPrimaryCollection);

                    midCollection.Collections.Add(subMidCollection);
                }
            }
        }

        internal void ChangeCurrentEditingPoint(Point point)
        {
            //find selected locatable

            var currentEditingPoint = this._primaryVerticesLayer.FindSelectedLocatable();

            if (currentEditingPoint == null)
                return;

            currentEditingPoint.X = point.X;

            currentEditingPoint.Y = point.Y;
        }

        private void MakePathGeometry()
        {
            _pathGeometry.Figures.Clear();

            MakePathGeometry(this._mercatorGeometry);
        }

        private void MakePathGeometry(Geometry geometry)
        {
            if (geometry.Points != null)
            {
                PathFigure pathFigure = new PathFigure() { IsClosed = _mercatorGeometry.IsRingBase() };

                if (geometry.Points.Length > 0)
                {
                    pathFigure.StartPoint = ToScreen(geometry.Points.First().AsWpfPoint());
                }

                for (int i = 1; i < geometry.Points.Length; i++)
                {
                    var segment = new LineSegment(ToScreen(geometry.Points[i].AsWpfPoint()), true);

                    pathFigure.Segments.Add(segment);
                }

                this._pathGeometry.Figures.Add(pathFigure);
            }
            else if (geometry.Geometries != null)
            {
                foreach (var g in geometry.Geometries)
                {
                    MakePathGeometry(g);
                }
            }
            else
            {
                return;
            }
        }

        private Locateable ToPrimaryLocateable(IPoint point)
        {
            var webMercatorPoint = point;

            var element = Options.MakePrimaryVertex();

            var locateable = new Locateable(Model.AncherFunctionHandlers.CenterCenter) { Element = element, X = webMercatorPoint.X, Y = webMercatorPoint.Y, Id = Guid.NewGuid() };

            locateable.RequestChangeIsSelected = (isSelected) =>
            {
                ((IMapMarker)locateable.Element).IsSelected = isSelected;
            };

            if (Options.IsNewDrawing)
            {
                //Finish Drawing if click on any point
                locateable.Element.MouseDown += (sender, e) =>
                {
                    if (e.LeftButton == MouseButtonState.Pressed)
                    {
                        this.OnRequestFinishDrawing.SafeInvoke(this);

                        e.Handled = true;
                    }
                };
            }
            else
            {
                element.MouseRightButtonDown += (sender, e) =>
                {
                    RegisterMapOptionsForVertices(e, webMercatorPoint, locateable);
                };
            }

            locateable.OnPositionChanged += (sender, e) =>
            {
                UpdateLineSegment(webMercatorPoint as Point, new Point(locateable.X, locateable.Y));

                webMercatorPoint.X = locateable.X;
                webMercatorPoint.Y = locateable.Y;

                UpdateEdgeLables();

                UpdateCoordinate(locateable);
            };

            return locateable;
        }

        private Locateable ToSecondaryLocateable(IPoint first, IPoint second)
        {
            var webMercatorPoint = new Point((first.X + second.X) / 2.0, (first.Y + second.Y) / 2.0);

            //var element = new Common.View.MapMarkers.Circle(.6);
            var element = Options.MakeSecondaryVertex();

            var locateable = new Locateable(Model.AncherFunctionHandlers.CenterCenter) { Element = element, X = webMercatorPoint.X, Y = webMercatorPoint.Y };

            element.MouseLeftButtonDown += (sender, e) =>
            {
                webMercatorPoint.X = locateable.X;

                webMercatorPoint.Y = locateable.Y;

                if (!TryInsertPoint(webMercatorPoint, first, second, this._mercatorGeometry))
                    throw new NotImplementedException();

                RequestRefresh?.Invoke(this);
            };

            return locateable;
        }

        private void RegisterMapOptionsForVertices(MouseButtonEventArgs e, IPoint point, Locateable locateable)
        {
            var presenter = new Jab.Common.Presenters.MapOptions.MapOptionsPresenter(
                rightToolTip: _copy,
                leftToolTip: _displayCoordinates,
                middleToolTip: _delete,

                rightSymbol: IRI.Jab.Common.Assets.ShapeStrings.Appbar.appbarClipboard,
                leftSymbol: IRI.Jab.Common.Assets.ShapeStrings.CustomShapes.xY,
                middleSymbol: IRI.Jab.Common.Assets.ShapeStrings.Appbar.appbarDelete);

            presenter.RightCommandAction = i =>
            {
                var geodetic = MapProjects.WebMercatorToGeodeticWgs84(presenter.Location);

                Clipboard.SetDataObject($"{geodetic.X.ToString("n4")},{geodetic.Y.ToString("n4")}");

                this.RemoveMapOptions();
            };

            presenter.LeftCommandAction = i =>
            {
                //RequestFinishEditing?.Invoke(this._mercatorGeometry);
                if (_primaryVerticesLabelLayer.Items.Any(l => l.Id == locateable.Id))
                {
                    _primaryVerticesLabelLayer.Remove(locateable.Id);
                }
                else
                {
                    var element = new Common.View.MapMarkers.CoordinateMarker(locateable.X, locateable.Y);

                    var auxLocateable = new Locateable(Model.AncherFunctionHandlers.CenterLeft) { Element = element, X = point.X, Y = point.Y, Id = locateable.Id };

                    _primaryVerticesLabelLayer.Items.Add(auxLocateable);
                }

                this.RemoveMapOptions();
            };

            presenter.MiddleCommandAction = i =>
            {
                this._primaryVerticesLabelLayer.Remove(locateable.Id);

                TryDeleteVertex(point, this._mercatorGeometry, _mercatorGeometry.Type == GeometryType.Polygon);

                this.RequestRefresh?.Invoke(this);

                this.RemoveMapOptions();
            };

            RequestRightClickOptions?.Invoke(new Common.View.MapOptions.MapThreeOptions(), e, presenter);

        }

        //private void RegisterMapOptionsForNewPath(MouseButtonEventArgs e)
        //{
        //    var presenter = new Jab.Common.Presenters.MapOptions.MapOptionsPresenter(
        //      rightToolTip: "لغو",
        //      leftToolTip: "تکمیل",
        //      middleToolTip: "تکمیل بخش",

        //  rightSymbol: IRI.Jab.Common.Assets.ShapeStrings.Appbar.appbarClose,
        //  leftSymbol: IRI.Jab.Common.Assets.ShapeStrings.Appbar.appbarCheck,
        //  middleSymbol: IRI.Jab.Common.Assets.ShapeStrings.Appbar.appbarCitySeattle);

        //    presenter.LeftCommandAction = i => { this.RequestCancelEditing?.Invoke(this); };
        //    presenter.RightCommandAction = i => { this.FinishEditing(); };
        //    presenter.MiddleCommandAction = i => { this.FinishDrawingPart(); };


        //    var view = new Common.View.MapOptions.MapThreeOptions(true);

        //    view.DataContext = presenter;

        //    RequestRightClickOptions?.Invoke(new Common.View.MapOptions.MapThreeOptions(false), e, presenter);
        //}

        private void RegisterMapOptionsForEditPath(MouseButtonEventArgs e)
        {
            var presenter = new Jab.Common.Presenters.MapOptions.MapOptionsPresenter(
                rightToolTip: string.Empty,
                leftToolTip: string.Empty,
                middleToolTip: string.Empty,

                leftSymbol: IRI.Jab.Common.Assets.ShapeStrings.Appbar.appbarClose,
                rightSymbol: IRI.Jab.Common.Assets.ShapeStrings.Appbar.appbarCheck,
                middleSymbol: IRI.Jab.Common.Assets.ShapeStrings.Appbar.appbarDelete);

            presenter.RightCommandAction = i =>
            {
                RequestFinishEditing?.Invoke(this._mercatorGeometry);

                this.RemoveMapOptions();
            };

            presenter.LeftCommandAction = i =>
            {
                RequestCancelEditing?.Invoke(this);

                this.RemoveMapOptions();
            };

            presenter.MiddleCommandAction = i =>
            {
                this.RequestCancelEditing?.Invoke(this);

                this.OnRequestDeleteGeometry?.Invoke(this, EventArgs.Empty);

                this.RemoveMapOptions();
            };

            RequestRightClickOptions?.Invoke(new Common.View.MapOptions.MapThreeOptions(false), e, presenter);

        }

        private void RemoveMapOptions()
        {
            this.RequestRemoveRightClickOptions?.Invoke();
        }

        private int GetLeftMidPointIndex(int primaryIndex, int length)
        {
            int left;

            if (primaryIndex == 0 && _mercatorGeometry.IsRingBase())
            {
                left = length - 1;
            }
            else if (primaryIndex == 0 && !_mercatorGeometry.IsRingBase())
            {
                left = int.MinValue;
            }
            else
            {
                left = primaryIndex - 1;
            }

            return left;
        }

        private int GetRightMidPointIndex(int primaryIndex, int length)
        {
            int right;

            if (primaryIndex == length && !_mercatorGeometry.IsRingBase())
            {
                right = int.MinValue;
            }
            else
            {
                right = primaryIndex;
            }

            return right;
        }

        private bool TryInsertPoint(Point webMercatorPoint, IPoint startLineSegment, IPoint endLineSegment, Geometry geometry)
        {
            var point = this.ToScreen(webMercatorPoint.AsWpfPoint());

            if (geometry.Points != null)
            {
                for (int i = 0; i < geometry.Points.Length; i++)
                {
                    if (geometry.Points[i] == startLineSegment)
                    {

                        geometry.InsertPoint(webMercatorPoint, i + 1);

                        ReconstructLocateables();

                        return true;
                    }
                }
            }
            else
            {
                for (int g = 0; g < geometry.Geometries.Length; g++)
                {
                    if (TryInsertPoint(webMercatorPoint, startLineSegment, endLineSegment, geometry.Geometries[g]))
                        return true;

                }
            }

            return false;
        }

        private bool TryDeleteVertex(IPoint point, Geometry geometry, bool isRing)
        {
            if (geometry.Points != null)
            {
                var minimumPoints = isRing ? 3 : 2;

                if (geometry.Points.Count() <= minimumPoints)
                    return false;

                for (int i = 0; i < geometry.Points.Length; i++)
                {
                    if (geometry.Points[i] == point)
                    {
                        geometry.Remove(geometry.Points[i]);

                        MakePathGeometry();

                        ReconstructLocateables();

                        return true;
                    }
                }
            }
            else
            {
                for (int g = 0; g < geometry.Geometries.Length; g++)
                {
                    if (TryDeleteVertex(point, geometry.Geometries[g], geometry.Type == GeometryType.Polygon))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TryDeleteVertex(double x, double y, Geometry geometry, bool isRing)
        {
            if (geometry.Points != null)
            {
                var minimumPoints = isRing ? 3 : 2;

                if (geometry.Points.Count() <= minimumPoints)
                    return false;

                geometry.Remove(x, y);

                MakePathGeometry();

                ReconstructLocateables();

                return true;

            }
            else
            {
                for (int g = 0; g < geometry.Geometries.Length; g++)
                {
                    if (TryDeleteVertex(x, y, geometry.Geometries[g], geometry.Type == GeometryType.Polygon))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void UpdateLineSegment(Point point, Point newValue)
        {
            var oldPoint = ToScreen(point.AsWpfPoint());

            var screenPoint = ToScreen(newValue.AsWpfPoint());

            for (int f = 0; f < this._pathGeometry.Figures.Count; f++)
            {
                var dif = this._pathGeometry.Figures[f].StartPoint - oldPoint;

                if (this._pathGeometry.Figures[f].StartPoint.AsPoint().AreExactlyTheSame(oldPoint.AsPoint()))
                {
                    this._pathGeometry.Figures[f].StartPoint = screenPoint;
                }
                else
                {
                    for (int s = 0; s < this._pathGeometry.Figures[f].Segments.Count; s++)
                    {
                        var lineSegment = (this._pathGeometry.Figures[f].Segments[s] as LineSegment);

                        dif = lineSegment.Point - oldPoint;

                        if (lineSegment.Point.AsPoint().AreExactlyTheSame(oldPoint.AsPoint()))
                        {
                            lineSegment.Point = screenPoint;
                        }
                    }
                }
            }

            var matched = UpdateLineSegment(point, newValue, this._mercatorGeometry, this._midVertices);

            if (!matched)
            {
                throw new NotImplementedException();
            }
        }

        private bool UpdateLineSegment(Point point, Point newPoint, Geometry geometry, RecursiveCollection<Locateable> midCollection)
        {
            if (geometry.Points != null)
            {
                for (int i = 0; i < geometry.Points.Length; i++)
                {
                    if (geometry.Points[i] == point)
                    {
                        UpdateMidPoints(point, newPoint, i, midCollection);

                        return true;
                    }
                }
            }
            else
            {
                for (int g = 0; g < geometry.Geometries.Length; g++)
                {
                    var matched = UpdateLineSegment(point, newPoint, geometry.Geometries[g], midCollection.Collections[g]);

                    if (matched)
                        return true;
                }
            }

            return false;
        }

        private void UpdateMidPoints(Point point, Point newPoint, int pointIndex, RecursiveCollection<Locateable> midCollection)
        {
            try
            {
                var displacement = new Point((newPoint.X - point.X) / 2.0, (newPoint.Y - point.Y) / 2.0);

                var length = midCollection.Values.Count;

                var leftIndex = GetLeftMidPointIndex(pointIndex, length);

                var rightIndex = GetRightMidPointIndex(pointIndex, length);

                Locateable left = leftIndex == int.MinValue ? null : midCollection.Values[leftIndex];

                if (left != null)
                {
                    left.X = left.X + displacement.X;
                    left.Y = left.Y + displacement.Y;
                }

                Locateable right = rightIndex == int.MinValue ? null : midCollection.Values[rightIndex];

                if (right != null)
                {
                    right.X = right.X + displacement.X;
                    right.Y = right.Y + displacement.Y;
                }

            }
            catch (Exception ex)
            {

            }
        }

        //probably this method can be better
        private void AddVertex(Point webMercatorPoint, Geometry geometry, RecursiveCollection<Locateable> primaryCollection)
        {
            if (geometry.Points != null)
            {
                var point = this.ToScreen(webMercatorPoint.AsWpfPoint());

                var locateable = ToPrimaryLocateable(webMercatorPoint);

                if (geometry.Points.Last().AreExactlyTheSame(webMercatorPoint))
                    return;

                geometry.AddLastPoint(webMercatorPoint);

                primaryCollection.Values.Add(locateable);

                this._primaryVerticesLayer.Items.Add(locateable);

                if (geometry.Points.Length > 1 && Options.IsEdgeLabelVisible)
                {
                    this._edgeLabelLayer.Items.Add(ToEdgeLengthLocatable(geometry.Points[geometry.Points.Length - 2], webMercatorPoint));
                }

            }
            else
            {
                AddVertex(webMercatorPoint, geometry.Geometries.Last(), primaryCollection.Collections.Last());
            }
        }

        private Locateable ToEdgeLengthLocatable(IPoint first, IPoint second)
        {
            Func<IPoint, IPoint> toGeodeticWgs84 = p => MapProjects.WebMercatorToGeodeticWgs84(p);

            var edge = new IRI.Ham.SpatialBase.Primitives.LineSegment(first, second);

            var element = new Common.View.MapMarkers.RectangleLabelMarker(edge.GetLengthLabel(toGeodeticWgs84));

            //var offset = _screenToMap(15);

            return new Locateable(Model.AncherFunctionHandlers.BottomCenter) { Element = element, X = edge.Middle.X, Y = edge.Middle.Y };
        }

        #endregion


        #region Public Methods

        public bool HasAnyPoint()
        {
            return this._mercatorGeometry != null ? this._mercatorGeometry.HasAnyPoint() : false;
        }

        public string MeasureLabel
        {
            get { return _mercatorGeometry.GetMeasureLabel(MapProjects.WebMercatorToGeodeticWgs84); }
        }

        public string AreaLabel
        {
            get { return UnitHelper.GetAreaLabel(_mercatorGeometry.GetArea(MapProjects.WebMercatorToGeodeticWgs84)); }
        }

        public string LengthLabel
        {
            get { return UnitHelper.GetAreaLabel(_mercatorGeometry.GetLength(MapProjects.WebMercatorToGeodeticWgs84)); }
        }

        public Path GetPath(Transform transform)
        {
            this._toScreen = transform;

            MakePathGeometry();

            return this._feature;
        }

        public SpecialPointLayer GetVertices()
        {
            //return new SpecialPointLayer("vert", this._vertices.SelectMany(i => i), ScaleInterval.All, LayerType.MoveableItem | LayerType.EditableItem, 1);
            return _primaryVerticesLayer;
        }

        public SpecialPointLayer GetMidVertices()
        {
            //return new SpecialPointLayer("int. vert", this._midVertices.SelectMany(i => i), ScaleInterval.All, LayerType.MoveableItem | LayerType.EditableItem, .7);
            return _midVerticesLayer;
        }

        public SpecialPointLayer GetEdgeLengthes()
        {
            UpdateEdgeLables();

            return _edgeLabelLayer;
        }

        public SpecialPointLayer GetPrimaryVerticesLabels()
        {
            return _primaryVerticesLabelLayer;
        }

        public Geometry GetFinalGeometry()
        {
            return this._mercatorGeometry;
        }

        public void AddVertex(Point webMercatorPoint)
        {
            AddVertex(webMercatorPoint, this._mercatorGeometry, this._vertices);
        }

        public void AddSemiVertex(Point webMercatorPoint)
        {
            var point = this.ToScreen(webMercatorPoint.AsWpfPoint());

            this._pathGeometry.Figures.Last().Segments.Add(new LineSegment(new WpfPoint(point.X, point.Y), true));
        }

        public void UpdateLastSemiVertexLocation(Point newMercatorPoint)
        {
            if (_pathGeometry.Figures?.Last()?.Segments?.Count() < 1)
                AddSemiVertex(newMercatorPoint);

            if (_pathGeometry.Figures.Last().Segments.Count < this._mercatorGeometry.GetLastPart().Length)
            {
                AddSemiVertex(newMercatorPoint);
            }

            var newPoint = this.ToScreen(newMercatorPoint.AsWpfPoint());

            var lastSegment = ((LineSegment)_pathGeometry.Figures.Last().Segments.Last()).Point = new WpfPoint(newPoint.X, newPoint.Y);
        }

        public void FinishEditing()
        {
            this._mercatorGeometry.ClearEmptyGeometries();

            this.RequestFinishEditing?.Invoke(this._mercatorGeometry);
        }

        private void GoToPreviousPoint()
        {
            this._primaryVerticesLayer.SelectPreviousLocatable();
        }

        private void GoToNextPoint()
        {
            this._primaryVerticesLayer.SelectNextLocatable();
        }

        private void TryDeleteCurrentPoint()
        {
            var locateable = _primaryVerticesLayer.FindSelectedLocatable();

            if (locateable == null)
                return;

            this._primaryVerticesLabelLayer.Remove(locateable.Id);

            TryDeleteVertex(locateable.X, locateable.Y, this._mercatorGeometry, _mercatorGeometry.Type == GeometryType.Polygon || _mercatorGeometry.Type == GeometryType.MultiPolygon);

            this.RequestRefresh?.Invoke(this);
        }

        private void ZoomToCurrentPoint()
        {
            var current = this._primaryVerticesLayer.FindSelectedLocatable();

            this.RequestZoomToPoint?.Invoke(new Point(current.X, current.Y));
        }

        private void CopyCurrentPointCoordinateToClipboard()
        {
            var current = this._primaryVerticesLayer.FindSelectedLocatable();

            var geodetic = MapProjects.WebMercatorToGeodeticWgs84(new Point(current.X, current.Y));

            Clipboard.SetDataObject($"{geodetic.X.ToString("n4")},{geodetic.Y.ToString("n4")}");

        }

        #endregion

        #region Commands

        private RelayCommand _finishEditingCommand;

        public RelayCommand FinishEditingCommand
        {
            get
            {
                if (_finishEditingCommand == null)
                {
                    _finishEditingCommand = new RelayCommand(param => this.RequestFinishEditing?.Invoke(this._mercatorGeometry));
                }

                return _finishEditingCommand;
            }
        }

        private RelayCommand _cancelEditingCommand;

        public RelayCommand CancelEditingCommand
        {
            get
            {
                if (_cancelEditingCommand == null)
                {
                    _cancelEditingCommand = new RelayCommand(param => this.RequestCancelEditing?.Invoke(this));
                }

                return _cancelEditingCommand;
            }
        }

        private RelayCommand _deleteCommand;

        public RelayCommand DeleteCommand
        {
            get
            {
                if (_deleteCommand == null)
                {
                    _deleteCommand = new RelayCommand(param =>
                    {
                        this.RequestCancelEditing?.Invoke(this);

                        this.OnRequestDeleteGeometry?.Invoke(this, EventArgs.Empty);
                    });
                }

                return _deleteCommand;
            }
        }

        private RelayCommand _cancelDrawingCommand;

        public RelayCommand CancelDrawingCommand
        {
            get
            {
                if (_cancelDrawingCommand == null)
                {
                    _cancelDrawingCommand = new RelayCommand(param => this.CancelDrawing());
                }

                return _cancelDrawingCommand;
            }
        }


        private RelayCommand _goToPreviousPointCommand;

        public RelayCommand GoToPreviousPointCommand
        {
            get
            {
                if (_goToPreviousPointCommand == null)
                {
                    _goToPreviousPointCommand = new RelayCommand(param => this.GoToPreviousPoint());
                }

                return _goToPreviousPointCommand;
            }
        }

        private RelayCommand _goToNextPointCommand;

        public RelayCommand GoToNextPointCommand
        {
            get
            {
                if (_goToNextPointCommand == null)
                {
                    _goToNextPointCommand = new RelayCommand(param => this.GoToNextPoint());
                }

                return _goToNextPointCommand;
            }
        }



        private RelayCommand _deleteCurrentPointCommand;

        public RelayCommand DeleteCurrentPointCommand
        {
            get
            {
                if (_deleteCurrentPointCommand == null)
                {
                    _deleteCurrentPointCommand = new RelayCommand(param => this.TryDeleteCurrentPoint());
                }

                return _deleteCurrentPointCommand;
            }
        }

        private RelayCommand _zoomToCurrentPointCommand;

        public RelayCommand ZoomToCurrentPointCommand
        {
            get
            {
                if (_zoomToCurrentPointCommand == null)
                {
                    _zoomToCurrentPointCommand = new RelayCommand(param => this.ZoomToCurrentPoint());
                }

                return _zoomToCurrentPointCommand;
            }
        }

        private RelayCommand _copyCurrentPointCommand;

        public RelayCommand CopyCurrentPointCommand
        {
            get
            {
                if (_copyCurrentPointCommand == null)
                {
                    _copyCurrentPointCommand = new RelayCommand(param => this.CopyCurrentPointCoordinateToClipboard());
                }

                return _copyCurrentPointCommand;
            }
        }





        #endregion
    }
}
