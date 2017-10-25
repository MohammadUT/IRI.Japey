﻿//BESMELLAHERAHMANERAHIM
//ALLAHOMAAJJELLEVALIEKALFARAJ

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Animation;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.SqlServer.Types;

using IRI.Jab.Common;
using IRI.Jab.Cartography;
using IRI.Jab.Cartography.Extensions;
using IRI.Jab.Cartography.Model;
using IRI.Jab.Common.Extensions;
using IRI.Jab.MapViewer.Model;
using IRI.Jab.Common.Model;
using sb = IRI.Ham.SpatialBase;
using IRI.Ham.SpatialBase.Mapping;
using IRI.Ham.Common;
using IRI.Ham.SpatialBase.Model;
using IRI.Ket.DataManagement.DataSource;
using IRI.Ket.DataManagement.Model;
using IRI.Ket.SpatialExtensions;

namespace IRI.Jab.MapViewer
{
    public partial class MapViewer : UserControl, INotifyPropertyChanged
    {
        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected void RaisePropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChangedEventHandler handler = PropertyChanged;

            if (handler != null)
                handler(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion


        #region Debugging

        const string _eventEntered = "(MapViewer) - EVENT RAISE";
        const string _eventLeaved = "(MapViewer) - EVENT LEAVED";
        const string _eventEscaped = "(MapViewer) - EVENT ESCAPE";
        const string _methodBegins = "(MapViewer) - METHOD BEGINS";
        const string _methodFinishes = "(MapViewer) - METHOD FINISHES";
        const string _methodEscaped = "(MapViewer) - METHOD ESCAPE";
        const string _refreshCalled = "(MapViewer) - REFRESH CALLED";
        const string _benchmarking = "(MapViewer) - BENCHMARKING";
        const string _info = "(MapViewer) - info";

        #endregion



        #region events

        public event MouseEventHandler OnMapMouseMove;

        public event EventHandler<ZoomEventArgs> OnZoomChanged;

        public event EventHandler<PointEventArgs> OnPointSelected;

        public event EventHandler<MapStatusEventArgs> OnStatusChanged;

        public event EventHandler<MapActionEventArgs> OnMapActionChanged;

        public event EventHandler OnExtentChanged;

        #endregion



        #region Fields, Properties

        private readonly object locker = new object();

        ExtentManager extentManager = new ExtentManager();

        LayerManager _layerManager = new LayerManager();

        List<Job> jobs = new List<Job>();

        //used to handle concurrent changes of zoomTransform.ScaleX/Y
        double _theScreenScale;

        public ObservableCollection<ILayer> Layers { get { return _layerManager.CurrentLayers; } }

        double baseScaleX, baseScaleY = -1;

        TransformGroup viewTransform = new TransformGroup();

        TranslateTransform panTransform = new TranslateTransform();

        ScaleTransform zoomTransform = new ScaleTransform();

        TranslateTransform panTransformForPoints = new TranslateTransform();

        public System.Net.WebProxy Proxy { get; set; }

        public double ScreenScale
        {
            //94.09.24: zoomTransform.ScaleX  may be at an animation
            //get { return this.zoomTransform.ScaleX * baseScaleX; }
            get { return this._theScreenScale * baseScaleX; }
        }

        private double _mapScale;

        public double MapScale
        {
            get
            {
                return _mapScale;
            }
            set
            {
                if (this._mapScale != value)
                {
                    this._mapScale = value;

                    RaisePropertyChanged();

                    Zoom(value);
                }
            }
        }

        //public bool UseDefaultCursorForMapAction { get; set; } = true;

        public Cursor PanCursor { get; set; } = Cursors.Hand;
        public Cursor ZoomInCursor { get; set; } = Cursors.Arrow;
        public Cursor ZoomOutCursor { get; set; } = Cursors.Arrow;
        public Cursor ZoomInRectangleCursor { get; set; } = Cursors.Cross;
        public Cursor ZoomOutRectangleCursor { get; set; } = Cursors.Cross;

        private Dictionary<MapAction, Cursor> CursorSettings;

        private MapAction _currentMouseAction = MapAction.Pan;

        public MapAction CurrentMouseAction
        {
            get { return _currentMouseAction; }
            set
            {
                this.SetCursor(CursorSettings[value]);

                if (_currentMouseAction == value)
                    return;

                _currentMouseAction = value;
                RaisePropertyChanged();

                //if (UseDefaultCursorForMapAction)
                //{
                //switch (value)
                //{
                //    case MapAction.Pan:
                //        this.SetCursor(PanCursor);
                //        break;
                //    case MapAction.ZoomIn:
                //        this.SetCursor(ZoomInCursor);
                //        break;
                //    case MapAction.ZoomOut:
                //        this.SetCursor(ZoomOutCursor);
                //        break;
                //    case MapAction.ZoomInRectangle:
                //        this.SetCursor(ZoomInRectangleCursor);
                //        break;
                //    case MapAction.ZoomOutRectangle:
                //        this.SetCursor(ZoomOutRectangleCursor);
                //        break;
                //    case MapAction.None:
                //        this.SetCursor(Cursors.AppStarting);
                //        break;
                //    default:
                //        break;
                //}
                //}

                this.OnMapActionChanged?.Invoke(null, new MapActionEventArgs(value));
            }
        }


        private MapStatus _status;

        public MapStatus Status
        {
            get { return _status; }
            set
            {
                _status = value;
                RaisePropertyChanged();
                this.OnStatusChanged?.Invoke(null, new MapStatusEventArgs(value));
            }
        }


        public bool IsGoogleZoomLevelsEnabled
        {
            get; set;
        }

        /// <summary>
        /// Based On Google Zoom Levels
        /// </summary>
        public int CurrentZoomLevel
        {
            get
            {
                var result = WebMercatorUtility.GetZoomLevel(this.MapScale);

                return result;
            }
        }

        private void UpdateTileInfos()
        {
            this.CurrentTileInfos = WebMercatorUtility.MercatorBoundingBoxToGoogleTileRegions(this.CurrentExtent, this.CurrentZoomLevel);
        }

        private List<TileInfo> _currentTileInfos;

        public List<TileInfo> CurrentTileInfos
        {
            get { return _currentTileInfos; }
            set
            {
                _currentTileInfos = value;

                this.extentManager.Update(_currentTileInfos);
            }
        }


        private Point _currentPoint;

        /// <summary>
        /// Current Mouse Position (longitude/Latitude) in decimal degree
        /// </summary>
        public Point CurrentPoint
        {
            get { return _currentPoint; }
            set
            {
                _currentPoint = value;
                RaisePropertyChanged();
            }
        }

        public sb.BoundingBox CurrentExtent
        {
            get
            {
                if (this.mapView.ActualHeight == 0 || this.mapView.ActualWidth == 0)
                {
                    return new sb.BoundingBox(0, 0, 0, 0);
                }

                Point p1 = new Point(0, 0);

                Point p2 = new Point(this.mapView.ActualWidth, this.mapView.ActualHeight);

                Point mapPoint1 = ScreenToMap(p1);

                Point mapPoint2 = ScreenToMap(p2);

                return new sb.BoundingBox(mapPoint1.X, mapPoint2.Y, mapPoint2.X, mapPoint1.Y);
            }
        }

        public MapViewer()
        {
            InitializeComponent();

            this.CursorSettings = new Dictionary<MapAction, Cursor>() {
                { MapAction.Pan, PanCursor },
                { MapAction.ZoomIn, ZoomInCursor },
                { MapAction.ZoomOut, ZoomOutCursor },
                { MapAction.ZoomInRectangle, ZoomInRectangleCursor },
                { MapAction.ZoomOutRectangle, ZoomOutRectangleCursor },
                { MapAction.None, Cursors.AppStarting },
            };

            _layerManager.OnRequestRefresh += (sender, e) =>
            {
                RefreshTilesButNotBaseMaps();

                Refresh();
            };

            this.extentManager.OnTilesAdded -= ExtentManager_OnTilesAdded;
            this.extentManager.OnTilesAdded += ExtentManager_OnTilesAdded;

            this.extentManager.OnTilesRemoved -= ExtentManager_OnTilesRemoved;
            this.extentManager.OnTilesRemoved += ExtentManager_OnTilesRemoved;

            baseScaleX = this.mapView.FlowDirection == FlowDirection.RightToLeft ? -1 : 1;

            this.zoomTransform.ScaleX = baseScaleX;
            this.zoomTransform.ScaleY = baseScaleY;

            this._theScreenScale = baseScaleX;

            this.viewTransform.Children.Add(panTransform);

            this.viewTransform.Children.Add(zoomTransform);

            this.mapView.MouseMove += (sender, e) =>
            {
                this.CurrentPoint = ScreenToGeodetic(e.GetPosition(this.mapView));

                this.OnMapMouseMove?.Invoke(sender, e);
            };

            this.OnZoomChanged += (sender, e) =>
            {
                this._mapScale = this.ToMapScale(this.ScreenScale);

                RaisePropertyChanged(nameof(MapScale));

                RaisePropertyChanged(nameof(CurrentZoomLevel));

                UpdateTileInfos();
            };
        }

        #endregion

        public void SetConnectionState(bool isConnectedToInternet)
        {
            foreach (var item in this.Layers)
            {
                if (item is TileServiceLayer)
                {
                    ((TileServiceLayer)item).IsOffline = !isConnectedToInternet;
                }
            }
        }


        public void Register(Cartography.Presenter.Map.MapPresenter presenter)
        {
            presenter.RequestSetProxy = (p) => this.Proxy = p;

            presenter.RequestGetActualHeight = () => this.mapView.ActualHeight;

            presenter.RequestGetActualWidth = () => this.mapView.ActualWidth;

            presenter.RegisterAction = (i) => { this.Register(i); };

            presenter.RequestSetDefaultCursor = (ma, c) => { this.SetDefaultCursor(ma, c); };

            presenter.RequestSetCursor = (c) => { this.SetCursor(c); };

            presenter.RequestRefresh = () => { this.Refresh(); };

            presenter.RequestIranExtent = () => { this.ZoomToExtent(sb.Primitives.BoundingBoxes.IranMercatorBoundingBox); };

            presenter.RequestFullExtent = () => this.FullExtent();

            presenter.RequestEnableRectangleZoom = () => { this.ZoomIn(); };

            presenter.RequestEnableZoomOut = () => { this.ZoomOutPoint(); };

            presenter.RequestEnableZoomOnMouseWheel = (e) =>
            {
                if (e)
                {
                    this.EnableZoomingOnMouseWheel();
                }
                else
                {
                    this.DisableZoomingOnMouseWheel();
                }
            };

            presenter.Layers = this.Layers;

            presenter.RequestSetConnectedState = (i) =>
            {
                //this.isOffline = !arg.Arg;
                //presenter.IsConnected = i;  //13960625

                this.SetConnectionState(i);
            };
            //bool isCachEnabled = false, string cacheDirectory = null, bool isOffline = false
            presenter.RequestChangeBaseMap = (provider, baseMapType, isCachEnabled, cacheDirectory, isOffline) =>
            {
                this.UnSetTileServices();

                this.SetTileService(provider, baseMapType, isCachEnabled, cacheDirectory, isOffline);

                this.RefreshTiles();
            };

            presenter.RequestMapScale = () => { return this.MapScale; };

            presenter.RequestCurrentExtent = () => { return this.CurrentExtent; };

            presenter.RequestCurrentZoomLevel = () => { return this.CurrentZoomLevel; };

            this.OnMapMouseMove += (sender, e) => { presenter.FireMouseMove(this.CurrentPoint); };

            this.OnZoomChanged += (sender, e) => { presenter.FireZoomChanged(this.MapScale); };

            this.OnExtentChanged += (sender, e) => { presenter.FireExtentChanged(this.CurrentExtent); };

            this.MouseUp += (sender, e) => { presenter.FireMapMouseUp(this.CurrentPoint); };

            this.OnStatusChanged += (sender, e) => { presenter.FireMapStatusChanged(e.Status); };

            this.OnMapActionChanged += (sender, e) => { presenter.FireMapActionChanged(e.Action); };

            presenter.RequestZoomToPoint = (center, mapScale) => this.Zoom(mapScale, center);

            presenter.RequestRegisterMapOptions = (arg) => { this.RegisterRightClickContextOptions(arg.View, arg.DataContext); };

            presenter.RequestRemoveMapOptions = () => { this.RemoveRightClickOptions(); };

            presenter.RequestUnregisterMapOptions = () => { this.UnregisterRightClickContextOptions(); };

            presenter.RequestPanTo = (point, callback) => { this.PanTo(point.X, point.Y, callback); };

            presenter.RequestFlashPoints = (i) => { this.Flash(i); };

            presenter.RequestFlashPoint = (i) => { this.Flash(i); };

            presenter.RequestZoomToExtent = (boundingBox, callback) => { this.ZoomToExtent(boundingBox, false, true, callback); };


            presenter.RequestGetGeometry = (mode, display) =>
            {
                return this.GetDrawingAsync(mode, display);
            };

            presenter.RequestCancelGetDrawing = () => this.CancelGetDrawing();

            presenter.RequestCancelEditGeometry = () => this.CancelEditGeometry();

            presenter.RequestFinishEditGeometry = () => this.FinishEditing();

            presenter.RequestGetBezier = async (geometry, decorationVisual) => { return await GetBezierAsync(geometry, decorationVisual); };

            presenter.RequestEdit = (g) => { return this.EditGeometryAsync(g); };

            presenter.RequestAddSpecialPointLayer = (i) =>
            {
                this.AddSpecialPointLayerToMap(i as SpecialPointLayer);
            };

            presenter.RequestSetLayer = (l) => { this.SetLayer(l); };

            presenter.RequestAddLayer = (l) =>
            {
                this.SetLayer(l);
                this.AddNonTiledLayer(l);
            };

            presenter.RequestRemoveLayer = (i) =>
            {
                this.RemoveLayer(i);
            };

            presenter.RequestRemoveLayerByName = (i) =>
              {
                  this.ClearLayer(i, true);
              };

            presenter.RequestRemovePolyBezierLayers = () =>
            {
                RemovePolyBezierLayers();
            };

            presenter.RequestAddPolyBezier = (name, points, geometry, showSymbolOnly, decorationVisuals) =>
              {
                  var layer = PolyBezierLayer.Create(name, points, this.viewTransform, geometry, decorationVisuals);

                  layer.IsControlsShown = !showSymbolOnly;

                  layer.IsDecorated = showSymbolOnly;

                  layer.IsBezierShown = !showSymbolOnly;

                  RegisterPolyBezierLayer(layer);

                  this.SetLayer(layer);

                  this.AddPolyBezierLayer(layer);
              };

            presenter.RequestAddGeometries = (g, n, p) =>
            {
                this.DrawGeometries(g, n, p);
            };

            presenter.RequestClearLayer = (t, r) => { this.ClearLayer(t, r); };

            presenter.RequestClearLayerByName = (l, r) => { this.ClearLayer(l, r); };

            presenter.RequestPan = () => { this.Pan(); };

            presenter.RequestZoomToFeature = f => { this.ZoomToFeature(f); };

            presenter.RequestIdentify = p => new ObservableCollection<System.Data.DataTable>(this.GetFeatures(p));

            presenter.RequestGetPoint = () => SelectPointAsync();
        }

        #region Conversions

        private double? _unitDistance;

        private double GetUnitDistance()
        {
            if (_unitDistance == null || double.IsNaN(_unitDistance.Value))
            {
                PresentationSource source = PresentationSource.FromVisual(this.mapView);

                if (source == null)
                    return 1;

                double dpiX = 96.0 * source.CompositionTarget.TransformToDevice.M11;

                //size of each pixel (in meter)
                _unitDistance = ConversionHelper.InchToMeterFactor / dpiX;
            }

            return _unitDistance.Value;
        }

        public double PixelSize
        {
            get { return GetUnitDistance(); }
        }

        private double ToScreenScale(double mapScale)
        {
            //PresentationSource source = PresentationSource.FromVisual(this.mapView);
            //double dpiX = 96.0 * source.CompositionTarget.TransformToDevice.M11;
            //double unitDistance = IRI.Ham.SpatialBase.ConstantValues.InchToMeterFactor / dpiX; // Meter
            //return mapScale / unitDistance;

            return mapScale / GetUnitDistance();
        }

        private double ToMapScale(double screenScale)
        {
            return screenScale * GetUnitDistance();
        }

        public Point MapToGeodetic(Point point)
        {
            return this.MercatorToGeodetic(point);
        }

        public Point GeodeticToMap(Point point)
        {
            return IRI.Ham.CoordinateSystem.MapProjection.MapProjects.GeodeticToMercator(point.AsPoint()).AsWpfPoint();
        }

        public Point ScreenToGeodetic(Point point)
        {
            return MapToGeodetic(ScreenToMap(point));
        }

        public Point GeodeticToScreen(Point point)
        {
            return MapToScreen(GeodeticToMap(point));
        }

        public Point ScreenToMap(Point point)
        {
            return this.viewTransform.Inverse.Transform(point);
        }

        public Point MapToScreen(Point point)
        {
            return this.viewTransform.Transform(point);
        }

        public double MapToScreen(double mercatorDistance)
        {
            return mercatorDistance * MapScale / GetUnitDistance();
        }

        private Point MercatorToGeodetic(Point point)
        {
            try
            {
                return IRI.Ham.CoordinateSystem.MapProjection.MapProjects.MercatorToGeodetic(point.AsPoint()).AsWpfPoint();
            }
            catch (Exception)
            {
                return new Point(double.NaN, double.NaN);
            }

        }

        #endregion


        #region Public Layer Management

        public void SetClusteredLayer(ScaleInterval scaleInterval, string imageDirectory, string layerName, Func<string, FrameworkElement> viewMaker, Action<object> mouseDownHandler = null)
        {
            var layer = ClusteredPointLayer.Create(imageDirectory, viewMaker);

            if (mouseDownHandler != null)
            {
                layer.OnRequestMouseDownHandle += (sender, e) => mouseDownHandler(sender);
            }

            layer.LayerName = layerName;

            layer.VisibleRange = scaleInterval;

            this._layerManager.Add(layer);

            this.AddComplexLayer(layer.GetLayer(MapScale), true);
        }

        public void SetRasterLayer(ScaleInterval scaleInterval, IDataSource dataSource, string layerName, double opacity, bool isBaseMap = false, bool isPyramid = false, RenderingApproach rendering = RenderingApproach.Default)
        {
            this._layerManager.Add(new RasterLayer(dataSource, layerName, scaleInterval, isBaseMap, isPyramid, Visibility.Visible, opacity, rendering));
        }

        public void SetTileService(ScaleInterval scaleInterval, Cartography.TileServices.MapProviderType provider, Cartography.TileServices.TileType type, bool isCachEnabled = false, string cacheDirectory = null, bool isOffline = false)
        {
            var layer = new TileServiceLayer(provider, type) { VisibleRange = scaleInterval };

            if (isCachEnabled && System.IO.Directory.Exists(cacheDirectory))
            {
                layer.EnableCaching(cacheDirectory);
            }

            layer.IsOffline = isOffline;

            this._layerManager.Add(layer);
        }

        public void SetTileService(Cartography.TileServices.MapProviderType provider, Cartography.TileServices.TileType type, bool isCachEnabled = false, string cacheDirectory = null, bool isOffline = false)
        {
            SetTileService(ScaleInterval.All, provider, type, isCachEnabled, cacheDirectory, isOffline);
        }

        public void UnSetTileService(Cartography.TileServices.MapProviderType provider, Cartography.TileServices.TileType type)
        {
            this._layerManager.Remove(provider, type);
        }

        public void UnSetTileServices()
        {
            this._layerManager.Remove(LayerType.BaseMap);
        }

        public void SetVectorLayer(
            ScaleInterval scaleInterval, IFeatureDataSource dataSource, string layerName, VisualParameters visualElements, RenderingApproach rendering = RenderingApproach.Default,
            bool isLabeled = false, Func<SqlGeometry, SqlGeometry> positionFunc = null, int fontSize = 0, Geometry pointSymbol = null)
        {
            LabelParameters parameters = new LabelParameters(null, fontSize, new SolidColorBrush(Colors.Black), new FontFamily("irannastaliq"), positionFunc);

            var layer = new VectorLayer(layerName, dataSource, visualElements, LayerType.VectorLayer, rendering, RasterizationApproach.DrawingVisual, scaleInterval, pointSymbol, isLabeled ? parameters : null);

            this._layerManager.Add(layer);
        }

        public void SetVectorLayer(
                ScaleInterval scaleInterval, IFeatureDataSource dataSource, string layerName, VisualParameters visualElements,
                LabelParameters parameters, RenderingApproach rendering = RenderingApproach.Default, Geometry pointSymbol = null, RasterizationApproach toRasterApproach = RasterizationApproach.GdiPlus)
        {
            if (toRasterApproach == RasterizationApproach.StreamGeometry && rendering == RenderingApproach.Tiled)
            {
                throw new NotImplementedException();
            }

            var layer = new VectorLayer(layerName, dataSource, visualElements, LayerType.VectorLayer, rendering, toRasterApproach, scaleInterval, pointSymbol, parameters);

            this._layerManager.Add(layer);
        }

        public void SetLayer(ILayer layer)
        {
            this._layerManager.Add(layer);
        }

        public void UnSetLayer(string layerName)
        {
            this._layerManager.Remove(layerName);

            Refresh();
        }

        public void SetSpecialPointLayer(ScaleInterval scaleInterval, string layerName, List<Locateable> items, double opacity = 1)
        {
            this._layerManager.Add(new SpecialPointLayer(layerName, items, opacity, scaleInterval, LayerType.Complex));
        }

        public void AddSpecialPointLayerToMap(ScaleInterval scaleInterval, string layerName, List<Locateable> items)
        {
            var specialLayer = new SpecialPointLayer(layerName, items, visibleRange: scaleInterval);

            this.SetLayer(specialLayer);

            this.AddComplexLayer(specialLayer);
        }

        public void AddSpecialPointLayerToMap(SpecialPointLayer layer)
        {
            this.SetLayer(layer);

            if (layer.VisibleRange.IsInRange(1.0 / this.MapScale))
            {
                this.AddComplexLayer(layer);
            }
        }

        public void AddSpecialLineLayerToMap(SpecialLineLayer layer)
        {
            this.SetLayer(layer);

            this.AddSpecialLineLayer(layer, null);
        }

        public void AddPolyBezierLayer(PolyBezierLayer layer)
        {
            if (!layer.IsBezierShown && !layer.IsDecorated)
            {
                throw new NotImplementedException();
            }

            layer.Redraw(this.viewTransform);

            if (layer.IsBezierShown)
            {
                var path = layer.GetMainPath();

                AddPanablePathToMapView(path);

                Canvas.SetZIndex(path, layer.ZIndex);

                AddComplexLayer(layer.GetMainPointLayer(), false);

                var controlPath = layer.GetControlPath();

                controlPath.Visibility = layer.IsControlsShown ? Visibility.Visible : Visibility.Collapsed;

                AddPanablePathToMapView(controlPath);

                Canvas.SetZIndex(controlPath, layer.ZIndex + 1);

                if (layer.IsControlsShown)
                {
                    AddComplexLayer(layer.GetControlPointLayer(), false);
                }
            }

        }

        #endregion


        #region Private Layer Management

        private async void AddTiledLayer(VectorLayer layer)
        {
            if (this.CurrentTileInfos == null)
                return;

            //Try #3
            layer.TileManager.Update(CurrentTileInfos.Select(i => i.Parse()).ToList());

            foreach (var region in CurrentTileInfos)
            {
                await AddTiledLayer(layer, region);
            }
        }

        private async void OldAddTiledLayer(VectorLayer layer, TileInfo tile)
        {
            var mapScale = MapScale;

            var extent = this.CurrentExtent;

            var layerTile = layer.TileManager.Find(tile);

            if (layerTile == null || layerTile.IsProcessing)
            {
                Debug.Print($"Layer escaped; Already is in process! {layer.LayerName} - {tile.ToShortString()}");
                return;
            }

            if (tile.ZoomLevel != this.CurrentZoomLevel)
            {
                Debug.Print($"Layer escaped! ZoomLevel Conflict 1 {layer.LayerName} - {tile.ToShortString()} expected zoomLevel:{this.CurrentZoomLevel}");
                layerTile.IsProcessing = false;
                return;
            }

            layerTile.IsProcessing = true;

            var geoLabelPair = await layer.GetGeometryLabelPairAsync(mapScale, tile.MercatorExtent);

            if (tile.ZoomLevel != this.CurrentZoomLevel || MapScale != mapScale)
            {
                Debug.Print($"Layer escaped! ZoomLevel Conflict 2 {layer.LayerName} - {tile.ToShortString()} expected zoomLevel:{this.CurrentZoomLevel}");
                layerTile.IsProcessing = false;
                return;
            }

            double tileScreenWidth = MapToScreen(tile.MercatorExtent.Width);

            double tileScreenHeight = MapToScreen(tile.MercatorExtent.Height);

            var area = ParseToRectangleGeometry(tile.MercatorExtent);

            //AddNonTiledLayer(new VectorLayer("", new List<SqlGeometry>() { tile.MercatorExtent.ToSqlGeometry(0) }, LayerType.VectorLayer, RenderingApproach.Default, RasterizationApproach.GdiPlus));

            Path pathImage;

            switch (layer.ToRasterTechnique)
            {
                case RasterizationApproach.DrawingVisual:
                    pathImage = layer.AsTileUsingDrawingVisual(geoLabelPair.Geometries, geoLabelPair.Labels, mapScale, tile, tileScreenWidth, tileScreenHeight, area, viewTransform, extent);
                    break;
                case RasterizationApproach.GdiPlus:
                    pathImage = layer.AsTileUsingGdiPlusAsync(geoLabelPair.Geometries, geoLabelPair.Labels, mapScale, tile, tileScreenWidth, tileScreenHeight, area, MapToScreen, extent);
                    break;
                case RasterizationApproach.WriteableBitmap:
                    pathImage = layer.AsTileUsingWriteableBitmap(geoLabelPair.Geometries, geoLabelPair.Labels, mapScale, tile, tileScreenWidth, tileScreenHeight, area, viewTransform, extent);
                    break;
                case RasterizationApproach.OpenTk:
                    pathImage = layer.AsTileUsinOpenTK(geoLabelPair.Geometries, geoLabelPair.Labels, mapScale, tile, tileScreenWidth, tileScreenHeight, area, viewTransform, extent);
                    break;
                case RasterizationApproach.StreamGeometry:
                //pathImage = layer.AsTileUsingStreamGeometry(geoLabelPair.Geometries, mapScale, tile, tileScreenWidth, tileScreenHeight, area, viewTransform, extent, this.panTransformForPoints);
                case RasterizationApproach.None:
                default:
                    throw new NotImplementedException();
            }

            if (tile.ZoomLevel != this.CurrentZoomLevel || MapScale != mapScale)
            {
                Debug.Print($"Layer escaped! ZoomLevel Conflict 3 {layer.LayerName} - {tile.ToShortString()} expected zoomLevel:{this.CurrentZoomLevel}");
                return;
            }

            if (pathImage != null)
            {
                this.mapView.Children.Add(pathImage);

                Canvas.SetZIndex(pathImage, layer.ZIndex);
            }

            layerTile.IsProcessing = false;
        }


        private async Task AddTiledLayer(VectorLayer layer, TileInfo tile)
        {
            var mapScale = MapScale;

            var extent = this.CurrentExtent;

            var _vt = viewTransform.Clone();



            var layerTile = layer.TileManager.Find(tile);

            if (layerTile == null || layerTile.IsProcessing)
            {
                Debug.Print($"Layer escaped; Already is in process! {layer.LayerName} - {tile.ToShortString()}");
                return;
            }

            if (tile.ZoomLevel != this.CurrentZoomLevel)
            {
                Debug.Print($"Layer escaped! ZoomLevel Conflict 1 {layer.LayerName} - {tile.ToShortString()} expected zoomLevel:{this.CurrentZoomLevel}");
                layerTile.IsProcessing = false;
                return;
            }

            layerTile.IsProcessing = true;

            var geoLabelPair = await layer.GetGeometryLabelPairAsync(mapScale, tile.MercatorExtent);

            if (tile.ZoomLevel != this.CurrentZoomLevel || MapScale != mapScale)
            {
                Debug.Print($"Layer escaped! ZoomLevel Conflict 2 {layer.LayerName} - {tile.ToShortString()} expected zoomLevel:{this.CurrentZoomLevel}");
                layerTile.IsProcessing = false;
                return;
            }

            double tileScreenWidth = MapToScreen(tile.MercatorExtent.Width);

            double tileScreenHeight = MapToScreen(tile.MercatorExtent.Height);

            var area = ParseToRectangleGeometry(tile.MercatorExtent);

            //AddNonTiledLayer(new VectorLayer("", new List<SqlGeometry>() { tile.MercatorExtent.ToSqlGeometry(0) }, LayerType.VectorLayer, RenderingApproach.Default, RasterizationApproach.GdiPlus));

            //var mapShift = new Point(tile.MercatorExtent.TopLeft.X - extent.TopLeft.X, tile.MercatorExtent.BottomRight.Y - extent.BottomRight.Y);



            Path pathImage;

            switch (layer.ToRasterTechnique)
            {
                case RasterizationApproach.DrawingVisual:
                    pathImage = layer.AsTileUsingDrawingVisual(geoLabelPair.Geometries, geoLabelPair.Labels, mapScale, tile, tileScreenWidth, tileScreenHeight, area, viewTransform, extent);
                    break;
                case RasterizationApproach.GdiPlus:
                    pathImage = layer.AsTileUsingGdiPlusAsync(geoLabelPair.Geometries, geoLabelPair.Labels, mapScale, tile, tileScreenWidth, tileScreenHeight, area, o => _vt.Transform(o), extent);
                    break;
                case RasterizationApproach.WriteableBitmap:
                    pathImage = layer.AsTileUsingWriteableBitmap(geoLabelPair.Geometries, geoLabelPair.Labels, mapScale, tile, tileScreenWidth, tileScreenHeight, area, viewTransform, extent);
                    break;
                case RasterizationApproach.OpenTk:
                    pathImage = layer.AsTileUsinOpenTK(geoLabelPair.Geometries, geoLabelPair.Labels, mapScale, tile, tileScreenWidth, tileScreenHeight, area, viewTransform, extent);
                    break;
                case RasterizationApproach.StreamGeometry:
                //pathImage = layer.AsTileUsingStreamGeometry(geoLabelPair.Geometries, mapScale, tile, tileScreenWidth, tileScreenHeight, area, viewTransform, extent, this.panTransformForPoints);
                case RasterizationApproach.None:
                default:
                    throw new NotImplementedException();
            }

            if (tile.ZoomLevel != this.CurrentZoomLevel || MapScale != mapScale)
            {
                Debug.Print($"Layer escaped! ZoomLevel Conflict 3 {layer.LayerName} - {tile.ToShortString()} expected zoomLevel:{this.CurrentZoomLevel}");
                return;
            }

            if (pathImage != null)
            {
                this.mapView.Children.Add(pathImage);

                Canvas.SetZIndex(pathImage, layer.ZIndex);
            }

            layerTile.IsProcessing = false;
        }


        private async void AddNonTiledLayer(VectorLayer layer)
        {
            if (this.CurrentTileInfos == null)
                return;

            #region Old Tries

            // Try #1
            //var image = layer.AsDrawing(this.MapScale, this.GetExactCurrentExtent(), this.mapView.ActualWidth, this.mapView.ActualHeight, this.MapToScreen);

            ////consider if layer was Labeled
            //var geometries = await layer.GetGeometries(this.MapScale, this.CurrentExtent);

            ////Try #2
            //var image = layer.AsBitmapUsingOpenTK(geometries, this.MapScale, this.GetExactCurrentExtent(), this.mapView.ActualWidth, this.mapView.ActualHeight, this.MapToScreen);

            //image.RenderTransform = viewTransformForPoints;

            //this.mapView.Children.Add(image);

            //Canvas.SetZIndex(image, this.mapView.Children.Count);

            #endregion

            //Try #3
            var extent = this.CurrentExtent;

            var mapScale = this.MapScale;

            //consider if layer was Labeled
            var geoLabledPairs = await layer.GetGeometryLabelPairAsync(mapScale, extent);

            if (this.MapScale != mapScale || this.CurrentExtent != extent)
                return;

            var area = ParseToRectangleGeometry(extent);

            Path path;

            switch (layer.ToRasterTechnique)
            {
                case RasterizationApproach.GdiPlus:
                    path = layer.AsBitmapUsingGdiPlus(geoLabledPairs.Geometries, geoLabledPairs.Labels, mapScale, extent, this.mapView.ActualWidth, this.mapView.ActualHeight, this.MapToScreen, area);
                    break;
                case RasterizationApproach.OpenTk:
                    path = layer.AsBitmapUsingOpenTK(geoLabledPairs.Geometries, geoLabledPairs.Labels, mapScale, extent, this.mapView.ActualWidth, this.mapView.ActualHeight, this.MapToScreen, area);
                    break;
                case RasterizationApproach.DrawingVisual:
                    path = layer.AsDrawingVisual(geoLabledPairs.Geometries, geoLabledPairs.Labels, mapScale, extent, this.mapView.ActualWidth, this.mapView.ActualHeight, this.MapToScreen, area);
                    break;
                case RasterizationApproach.WriteableBitmap:
                    path = layer.AsBitmapUsingWriteableBitmap(geoLabledPairs.Geometries, geoLabledPairs.Labels, mapScale, extent, this.mapView.ActualWidth, this.mapView.ActualHeight, this.MapToScreen, area);
                    break;
                case RasterizationApproach.StreamGeometry:
                    path = layer.AsShape(geoLabledPairs.Geometries, mapScale, extent, this.mapView.ActualWidth, this.mapView.ActualHeight,
                        this.viewTransform,
                        this.panTransformForPoints,
                        this.MapToScreen);
                    break;
                case RasterizationApproach.None:
                default:
                    throw new NotImplementedException();
            }

            if (path == null || this.MapScale != mapScale || this.CurrentExtent != extent)
                return;

            this.mapView.Children.Add(path);

            Canvas.SetZIndex(path, layer.ZIndex);
        }

        private RectangleGeometry ParseToRectangleGeometry(sb.BoundingBox mapBoundingBox)
        {
            var p1 = mapBoundingBox.TopLeft.AsWpfPoint();

            var p2 = mapBoundingBox.BottomRight.AsWpfPoint();

            RectangleGeometry geometry = new RectangleGeometry(new Rect(p1, p2), 0, 0);

            geometry.Transform = viewTransform;

            return geometry;
        }


        private void AddSpecialLineLayer(SpecialLineLayer layer, Action mouseDown = null)
        {
            var paths = layer.GetPaths(this.viewTransform, this.CurrentExtent, mouseDown);

            foreach (var item in paths)
            {
                (item.RenderTransform as TransformGroup).Children.Add(this.panTransformForPoints);

                item.Tag = new LayerTag(0) { IsTiled = false, Layer = layer, LayerType = LayerType.AnimatingItem };

                if (!this.mapView.Children.Contains(item))
                {
                    this.mapView.Children.Add(item);

                    Canvas.SetZIndex(item, int.MaxValue);
                }
            }

        }

        private void AddEditableFeatureLayer(EditableFeatureLayer layer)
        {
            var path = layer.GetPath(this.viewTransform);

            //path.RenderTransform = this.viewTransform;
            path.RenderTransform = this.panTransformForPoints;

            path.Tag = new LayerTag(0) { Layer = layer, IsTiled = false, LayerType = LayerType.EditableItem };

            this.mapView.Children.Add(path);

            Canvas.SetZIndex(path, layer.ZIndex);

            AddComplexLayer(layer.GetMidVertices(), false);

            AddComplexLayer(layer.GetVertices(), false);
        }

        //POTENTIALLY ERROR PROUNE; What if the Element has no scaletransform

        //public void AddMoveableItem(Locateable item)
        //{
        //    var element = item.Element;

        //    var screenLocation = item.AncherFunction(
        //        MapToScreen(new Point(item.X, item.Y)), element.Width, element.Height);

        //    //element.RenderTransformOrigin = new Point(.5, .5);
        //    var tempPoint = item.AncherFunction(new Point(0, 0), element.Width, element.Height);

        //    element.RenderTransformOrigin = new Point(-tempPoint.X / element.Width, -tempPoint.Y / element.Height);

        //    var scaleTransform = ((TransformGroup)(element.RenderTransform)).Children.First();
        //    ((TransformGroup)(element.RenderTransform)).Children.Clear();

        //    ((TransformGroup)(element.RenderTransform)).Children.Add(scaleTransform);

        //    ((TransformGroup)(element.RenderTransform)).Children.Add(this.panTransformForPoints);

        //    ((TransformGroup)(element.RenderTransform)).Children.Add(new TranslateTransform(screenLocation.X, screenLocation.Y));

        //    element.Tag = new LayerTag(this.MapScale) { Layer = null, IsTiled = false, LayerType = LayerType.MoveableItem };

        //    element.MouseDown -= Element_MouseDownForMoveableItem;
        //    element.MouseDown += Element_MouseDownForMoveableItem;

        //    AddToCanvasWithAnimation(element, element.Opacity);
        //}

        bool itemIsMoving = false;

        FrameworkElement currentMoveableItem;

        //Point startMapPointLocation;

        public void Element_MouseDownForMoveableItem(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;

            itemIsMoving = true;

            var element = sender as FrameworkElement;

            this.currentMoveableItem = element;

            this.mapView.CaptureMouse();

            this.mapView.MouseMove -= Element_MouseMoveForMoveableItem;
            this.mapView.MouseMove += Element_MouseMoveForMoveableItem;

            this.mapView.MouseUp -= Element_MouseUpForMoveableItem;
            this.mapView.MouseUp += Element_MouseUpForMoveableItem;


            this.prevMouseLocation = (e.GetPosition(this.mapView));

            this.startPointLocationForPan = this.prevMouseLocation;

            //this.startMapPointLocation = ScreenToMap(this.prevMouseLocation);
        }

        public void Element_MouseMoveForMoveableItem(object sender, MouseEventArgs e)
        {
            //var translateTransform = ((TransformGroup)(currentMoveableItem.RenderTransform)).Children[2] as TranslateTransform;

            Point currentMouseLocation = (e.GetPosition(this.mapView));

            var currentMapLocation = ScreenToMap(currentMouseLocation);

            var prevMapLocation = ScreenToMap(prevMouseLocation);

            var layer = ((this.currentMoveableItem.Tag as LayerTag).Layer as SpecialPointLayer);

            var locateable = layer.Get(this.currentMoveableItem);

            if (locateable != null)
            {
                locateable.X += currentMapLocation.X - prevMapLocation.X;
                locateable.Y += currentMapLocation.Y - prevMapLocation.Y;
            }

            //translateTransform.X += currentMouseLocation.X - this.prevMouseLocation.X;

            //translateTransform.Y += currentMouseLocation.Y - this.prevMouseLocation.Y;

            this.prevMouseLocation = currentMouseLocation;

        }

        public void Element_MouseUpForMoveableItem(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;

            Point currentMouseLocation = ScreenToMap((e.GetPosition(this.mapView)));

            var prevMapLocation = ScreenToMap(prevMouseLocation);

            var offset = new Point(currentMouseLocation.X - prevMapLocation.X, currentMouseLocation.Y - prevMapLocation.Y);

            var layer = ((this.currentMoveableItem.Tag as LayerTag).Layer as SpecialPointLayer);

            var locateable = layer.Get(this.currentMoveableItem);

            if (locateable != null)
            {
                locateable.X += offset.X;
                locateable.Y += offset.Y;
            }

            this.mapView.MouseMove -= Element_MouseMoveForMoveableItem;

            this.mapView.MouseUp -= Element_MouseUpForMoveableItem;

            itemIsMoving = false;

            this.mapView.ReleaseMouseCapture();

        }

        //POTENTIALLY ERROR PROUNE; What if the Element has no scaletransform
        private void Item_OnPositionChanged(object sender, EventArgs e)
        {
            var item = sender as Locateable;

            var element = item.Element;

            var screenLocation = item.AncherFunction(MapToScreen(new Point(item.X, item.Y)), element.Width, element.Height);

            ((TransformGroup)(element.RenderTransform)).Children[2] = (new TranslateTransform(screenLocation.X, screenLocation.Y));

        }

        //POTENTIALLY ERROR PROUNE; What if the Element has no scaletransform
        private void AddComplexLayer(SpecialPointLayer specialPointLayer, bool withAnimation = true)
        {
            specialPointLayer.HandleCollectionChanged = (e) =>
            {
                switch (e.Action)
                {
                    case System.Collections.Specialized.NotifyCollectionChangedAction.Add:
                        foreach (var item in e.NewItems)
                        {
                            AddComplexLayerItem(specialPointLayer, (Locateable)item, withAnimation);
                        }
                        break;
                    case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
                        foreach (var item in e.OldItems)
                        {
                            var locateable = item as Locateable;

                            if (this.mapView.Children.Contains(locateable.Element))
                                this.mapView.Children.Remove(locateable.Element);
                        }
                        break;

                    case System.Collections.Specialized.NotifyCollectionChangedAction.Reset:
                        ClearLayer(specialPointLayer);
                        break;

                    case System.Collections.Specialized.NotifyCollectionChangedAction.Replace:
                    case System.Collections.Specialized.NotifyCollectionChangedAction.Move:
                    default:
                        throw new NotImplementedException();
                }
            };

            var items = specialPointLayer.Items.Where(i => this.CurrentExtent.Intersects(new sb.Point(i.X, i.Y)));

            foreach (var item in items)
            {
                AddComplexLayerItem(specialPointLayer, item, withAnimation);
            }
        }

        //POTENTIALLY ERROR PRONOUN
        private void AddComplexLayerItem(SpecialPointLayer specialPointLayer, Locateable item, bool withAnimation = true)
        {
            item.OnPositionChanged -= Item_OnPositionChanged;
            item.OnPositionChanged += Item_OnPositionChanged;

            var element = item.Element;

            element.Opacity = specialPointLayer.VisualParameters.Opacity;

            var screenLocation = item.AncherFunction(MapToScreen(new Point(item.X, item.Y)), element.Width, element.Height);

            //element.RenderTransformOrigin = new Point(.5, .5);
            var tempPoint = item.AncherFunction(new Point(0, 0), element.Width, element.Height);

            element.RenderTransformOrigin = new Point(-tempPoint.X / element.Width, -tempPoint.Y / element.Height);

            var scaleTransform = ((TransformGroup)(element.RenderTransform)).Children.First();

            ((TransformGroup)(element.RenderTransform)).Children.Clear();

            ((TransformGroup)(element.RenderTransform)).Children.Add(scaleTransform);

            ((TransformGroup)(element.RenderTransform)).Children.Add(this.panTransformForPoints);

            ((TransformGroup)(element.RenderTransform)).Children.Add(new TranslateTransform(screenLocation.X, screenLocation.Y));

            //What about other types: RightClickOption, GridAndGraticule
            if (specialPointLayer.Type.HasFlag(LayerType.MoveableItem))
            {
                element.Tag = new LayerTag(this.MapScale) { Layer = specialPointLayer, IsTiled = false, LayerType = specialPointLayer.Type };

                element.MouseLeftButtonDown -= Element_MouseDownForMoveableItem;
                element.MouseLeftButtonDown += Element_MouseDownForMoveableItem;
            }
            else if (specialPointLayer.Type.HasFlag(LayerType.Complex) || specialPointLayer.Type.HasFlag(LayerType.EditableItem))
            {
                element.Tag = new LayerTag(this.MapScale) { Layer = specialPointLayer, IsTiled = false, LayerType = specialPointLayer.Type };
            }
            else
            {
                throw new NotImplementedException();
            }

            if (withAnimation)
            {
                AddToCanvasWithAnimation(element, element.Opacity, specialPointLayer.AlwaysTop);
            }
            else
            {
                if (this.mapView.Children.Contains(element))
                    return;

                this.mapView.Children.Add(element);

                if (specialPointLayer.AlwaysTop)
                {
                    Canvas.SetZIndex(element, int.MaxValue);
                }

            }
        }

        private void AddToCanvasWithAnimation(FrameworkElement element, double finalOpacity, bool insertTop = true)
        {
            if (this.mapView.Children.Contains(element))
                return;

            DoubleAnimation animation = new DoubleAnimation()
            {
                From = 0,
                To = finalOpacity,
                Duration = new Duration(TimeSpan.FromMilliseconds(1000)),
                FillBehavior = FillBehavior.Stop,
                AccelerationRatio = .2,
                DecelerationRatio = .2
            };

            this.mapView.Children.Add(element);

            if (insertTop)
            {
                Canvas.SetZIndex(element, int.MaxValue);
            }

            element.BeginAnimation(OpacityProperty, animation);
        }

        //StreamGeometry Approach
        //private async Task AddLayerAsShape(VectorLayer layer)
        //{
        //    var mapScale = MapScale;

        //    var extent = CurrentExtent;

        //    var geoLabelPairs = await layer.GetGeometryLabelPair(mapScale, extent);

        //    if (mapScale != MapScale || CurrentExtent != extent)
        //        return;

        //    var result =
        //        layer.AsShape(geoLabelPairs.Geometries, mapScale,
        //            extent,
        //            this.mapView.ActualWidth, this.mapView.ActualHeight,
        //            this.viewTransform,
        //            new TransformGroup() { Children = new TransformCollection() { this.panTransformForPoints } },
        //            p => this.MapToScreen(p.AsWpfPoint()).AsPoint());

        //    //if (result.Item1 != null)
        //    //{
        //    //    this.mapView.Children.Add(result.Item1);

        //    //    Canvas.SetZIndex(result.Item1, int.MaxValue);
        //    //}

        //    this.mapView.Children.Add(result);

        //    Canvas.SetZIndex(result, int.MaxValue);
        //}

        private async void AddLayer(RasterLayer layer, sb.BoundingBox boundingBox)
        {
            //94.06.28
            //////////if (!this.Layers.Contains(layer))
            //////////{
            //////////    this.Layers.Add(layer);
            //////////}


            var paths = await layer.ParseToPath(boundingBox, this.viewTransform, this.MapScale, GetUnitDistance());
            //var paths = await layer.ParseToPath(boundingBox, this.mapTransform, this.viewTransform, this.MapScale, GetUnitDistance());

            Debug.WriteLine(string.Format("Number of Images for layer: {0} was {1}", layer.LayerName, paths.Count));

            foreach (var item in paths)
            {
                var tag = item.Tag as LayerTag;

                //if (tag != null)
                //{
                //    (item.Tag as LayerTag).LayerType = layer.Type;
                //}

                if (layer.Type == LayerType.BaseMap)
                {
                    this.mapView.Children.Insert(0, item);
                }
                else
                {
                    this.mapView.Children.Add(item);
                }
            }
        }

        private async Task AddLayerAsync(TileServiceLayer layer, TileInfo tile)
        {
            if (tile.ZoomLevel != CurrentZoomLevel)
            {
                Debug.Print($"TileServiceLayer escaped! ZoomLevel Conflict 1 {layer.LayerName} - {tile.ToShortString()} expected zoomLevel:{this.CurrentZoomLevel}");
                return;
            }

            var geoImage = await layer.GetTileAsync(tile, this.Proxy);

            if (tile.ZoomLevel != CurrentZoomLevel)
            {
                Debug.Print($"TileServiceLayer escaped! ZoomLevel Conflict 2 {layer.LayerName} - {tile.ToShortString()} expected zoomLevel:{this.CurrentZoomLevel}");
                return;
            }

            var mercatorExtent = geoImage.GeodeticWgs84BoundingBox.Transform(i => IRI.Ham.CoordinateSystem.MapProjection.MapProjects.GeodeticToMercator(i));

            Point topLeft = mercatorExtent.TopLeft.AsWpfPoint();

            Point bottomRigth = mercatorExtent.BottomRight.AsWpfPoint();

            RectangleGeometry geometry = new RectangleGeometry(new Rect(topLeft, bottomRigth), 0, 0);

            geometry.Transform = viewTransform;

            //94.12.16
            //int width = (int)(mercatorExtent.Width * this.MapScale / this.GetUnitDistance());

            //int height = (int)(mercatorExtent.Height * this.MapScale / this.GetUnitDistance());

            ImageBrush fill;

            try
            {
                fill = new ImageBrush(IRI.Jab.Common.Helpers.ImageUtility.ToImage(geoImage.Image));
            }
            catch (Exception ex)
            {
                fill = new ImageBrush();
                Trace.WriteLine($"MapViewer; AddLayerAsync(TileServiceLayer) {ex.Message}");
            }

            Path path = new Path()
            {
                Fill = fill,
                Data = geometry,
                Tag = new LayerTag(this.MapScale) { Layer = layer, Tile = tile }
                //Tag = new LayerTag(GoogleMapsUtility.GetGoogleMapScale(tile.ZoomLevel)) { Layer = layer, LayerType = layer.Type, Tile = tile }
            };

            layer.Element = path;

            //94.09.04
            //var action = new Action(() =>
            //{
            if (layer.Type == LayerType.BaseMap)
            {
                this.mapView.Children.Insert(0, path);
            }
            else
            {
                this.mapView.Children.Add(path);
            }

        }

        #endregion


        public void RefreshTilesButNotBaseMaps()
        {
            this.ClearTiledButNotBaseMaps();

            var tiles = this.CurrentTileInfos;

            if (tiles == null)
                return;

            foreach (var tile in tiles)
            {
                RefreshTiles(tile, false);
            }
        }

        public void RefreshTiles()
        {

            this.ClearTiled();

            var tiles = this.CurrentTileInfos;

            foreach (var tile in tiles)
            {
                RefreshTiles(tile);
            }

        }

        public void RefreshTiles(TileInfo tile, bool processBaseMaps = true)
        {
            IEnumerable<ILayer> infos = this._layerManager.UpdateAndGetLayers(1.0 / MapScale).ToList();

            Action action = async () =>
           {
               foreach (ILayer item in infos)
               {
                   if (item.VisualParameters.Visibility != Visibility.Visible)
                       continue;

                   if (!this.CurrentTileInfos.Contains(tile))
                   {
                       Debug.Print("Not in `CurrentTileInfos` [@RefreshTiles(TileInfo tile)]");
                       return;
                   }

                   if (item.Rendering != RenderingApproach.Tiled)
                       continue;

                   //Do not draw base map in the case of change visibility for layers
                   if (item.Type == LayerType.BaseMap && !processBaseMaps)
                       continue;

                   //Debug.Print($"{item.LayerName} - {tile.ToShortString()}");

                   if (item is VectorLayer)
                   {
                       VectorLayer vectorLayer = (VectorLayer)item;

                       vectorLayer.TileManager.TryAdd(tile);

                       await AddTiledLayer(vectorLayer, tile);
                   }
                   else if (item is TileServiceLayer)
                   {
                       await AddLayerAsync(item as TileServiceLayer, tile);
                   }
                   else
                   {
                       //return;
                       throw new NotImplementedException();
                   }

               }
           };

            Task.Run(() =>
            {
                lock (locker)
                {
                    this.jobs.Add(
                            new Job(
                                new LayerTag(this.MapScale) { LayerType = LayerType.None, Tile = tile },
                                Dispatcher.BeginInvoke(action, DispatcherPriority.Background)));
                }
            });

            ////94.09.17
            //Task.Run(() =>
            // this.jobs.Add(
            //     new Job(
            //         new LayerTag(WebMercatorUtility.GetGoogleMapScale(tile.ZoomLevel)) { LayerType = LayerType.None, Tile = tile },
            //         Dispatcher.BeginInvoke(action, DispatcherPriority.Background)))
            //                        );
        }


        //POTENTIALLY ERROR PROUNE; Check if exceptions are catched correctly; 
        //POTENTIALLY ERROR PROUNE; Captured Variables
        //IMPROVEMENT; use vector approach for light vector layers insted of "AddLayerAsDrawing"
        public void Refresh()
        {
            //UpdateTileInfos();

            if (this.CurrentTileInfos == null)
            {
                return;
            }

            StopUnnecessaryJobs();

            ClearNonTiled();

            var mapScale = this.MapScale;

            IEnumerable<ILayer> infos = this._layerManager.UpdateAndGetLayers(1.0 / MapScale);

            if (infos == null) return;

            foreach (ILayer item in infos)
            {
                if (item.VisualParameters.Visibility != Visibility.Visible)
                    continue;

                if (MapScale != mapScale)
                {
                    Debug.Print("Zoom Level Conflict! [@Refresh foreach loop]");

                    return;
                }

                if (item.Rendering == RenderingApproach.Tiled)
                    continue;


                if (item is ClusteredPointLayer)
                {
                    Action action = () =>
                    {
                        AddComplexLayer((item as ClusteredPointLayer).GetLayer(mapScale));
                    };

                    Task.Run(() => this.jobs.Add(new Job(new LayerTag(mapScale) { LayerType = LayerType.Complex, Tile = null },
                        Dispatcher.BeginInvoke(action, DispatcherPriority.Background, null))));
                }
                else if (item is DrawingLayer)
                {
                    var layer = (item as DrawingLayer).GetLayer();

                    AddEditableFeatureLayer(layer);
                }
                else if (item is EditableFeatureLayer)
                {
                    var layer = item as EditableFeatureLayer;

                    AddEditableFeatureLayer(layer);
                }
                else if (item is PolyBezierLayer)
                {
                    var layer = item as PolyBezierLayer;

                    AddPolyBezierLayer(layer);
                }
                else if (item is SpecialLineLayer)
                {
                    var layer = item as SpecialLineLayer;

                    AddSpecialLineLayer(layer, null);
                }
                else if (item.Type.HasFlag(LayerType.Complex) || item.Type.HasFlag(LayerType.MoveableItem))
                {
                    Action action = () =>
                    {
                        AddComplexLayer((SpecialPointLayer)item);
                    };

                    Task.Run(() =>
                      this.jobs.Add(new Job(new LayerTag(mapScale) { LayerType = LayerType.Complex, Tile = null },
                          Dispatcher.BeginInvoke(action, DispatcherPriority.Background, null)))
                      );

                    //this.tasks.Add(task);
                }
                else if (item is TileServiceLayer)
                {
                    //their Rendering property must be Tiled and catched by the first `if`
                    throw new NotImplementedException();

                    //They are handled when UpdateTileInfos is fired
                    //continue;
                }
                else if (!item.Type.HasFlag(LayerType.Raster) && !item.Type.HasFlag(LayerType.BaseMap) && !item.Type.HasFlag(LayerType.ImagePyramid))
                {
                    VectorLayer vectorLayer = (VectorLayer)item;

                    Action action = () =>
                    {
                        //AddTiledLayer(vectorLayer);
                        AddNonTiledLayer(vectorLayer);
                        //ClearBasemap();
                    };

                    var extent = this.CurrentExtent;

                    Task.Run(() =>
                       this.jobs.Add(new Job(
                          new LayerTag(mapScale) { LayerType = LayerType.VectorLayer, BoundingBox = extent },
                          Dispatcher.BeginInvoke(action, DispatcherPriority.Background, null)))
                      );

                    //this.tasks.Add(task);
                }
                else if (!item.Type.HasFlag(LayerType.Label))
                {
                    ////this.jobs.Add(
                    ////Dispatcher.BeginInvoke(new Action(() =>
                    ////{
                    ////AddLayer((RasterLayer)item, item.Type == LayerType.Tile ? this.CurrentExtent : this.GetExactCurrentExtent());
                    //AddLayer((RasterLayer)item, this.CurrentExtent);
                    ////})));

                    if (item.Rendering == RenderingApproach.Default)
                    {
                        AddLayer((RasterLayer)item, this.CurrentExtent);

                        //Remove Old base maps
                        ClearOutOfExtent(false);

                        //Task.Run(() =>
                        //{
                        //    this.jobs.Add(new Job(
                        //       new LayerTag() { LayerType = item.Type, Extent = null },
                        //       Dispatcher.BeginInvoke(new Action(async () => { await AddLayer((RasterLayer)item, this.CurrentExtent); ClearBasemap(); }), DispatcherPriority.Background, null)));
                        //});
                    }
                }
                else
                {
                    throw new NotImplementedException();
                }

            }
            //****************************

            //ResetViewTransformForPoints();
            this.panTransformForPoints.X = 0;
            this.panTransformForPoints.Y = 0;

            //Dispatcher.BeginInvoke(new Action(() =>
            //{
            //    ClearOld();

            //}), System.Windows.Threading.DispatcherPriority.ContextIdle, null);
            this.OnExtentChanged?.Invoke(null, EventArgs.Empty);
        }


        #region Others
        //POTENTIALLY ERROR PROUNE; not sure everything is considered or not
        private void ResetMapViewEvents()
        {
            this.SetCursor(Cursors.Arrow);

            this.mapView.MouseDown -= mapView_MouseDownForPan;
            this.mapView.MouseUp -= mapView_MouseUpForPan;
            this.mapView.MouseMove -= mapView_MouseMoveForPan;

            this.mapView.MouseUp -= mapView_MouseDownForZoomOut;
            this.mapView.MouseDown -= mapView_MouseDownForZoom;
            this.mapView.MouseUp -= mapView_MouseUpForZoom;

            //this.mapView.MouseDown -= mapView_MouseDownForStartDrawing;
            //this.mapView.MouseDown -= MapView_MouseDownForPanWhileDraw;
            //this.mapView.MouseMove -= mapView_MouseMoveForDraw;
            //this.mapView.MouseUp -= mapView_MouseUpForDraw;

            this.mapView.MouseDown -= mapView_MouseUpForSelectPoint;
            this.mapView.MouseUp -= mapView_MouseUpForSelectPoint;

            this.mapView.MouseDown -= MapView_MouseDownForStartDrawing;
            this.mapView.MouseDown -= MapView_MouseDownForPanWhileDrawing;
            this.mapView.MouseMove -= MapView_MouseMoveDrawing;
            this.mapView.MouseUp -= MapView_MouseUpForDrawing;

        }

        public void ReleaseEvents()
        {
            this.ResetMapViewEvents();
        }

        public void SetDefaultCursor(MapAction action, Cursor cursor)
        {
            this.CursorSettings[action] = cursor;
        }

        public void SetCursor(Cursor cursor)
        {
            this.mapView.Cursor = cursor;
        }

        #endregion


        #region Clearing-Removing layers, Job Management

        private void StopUnnecessaryJobs()
        {
            lock (locker)
            {
                for (int i = this.jobs.Count - 1; i >= 0; i--)
                {
                    var currentJob = this.jobs[i];

                    if (currentJob == null)
                    {
                        this.jobs.Remove(currentJob);

                        continue;
                    }

                    //Do not cancel processing tiles when mouse up for pan
                    //if (this.jobs[i].Tag.Tile != null && this.CurrentTileInfos.Contains(jobs[i].Tag.Tile) && jobs[i].Tag.Scale == this.MapScale)
                    if (this.CurrentTileInfos.Contains(currentJob.Tag.Tile) && currentJob.Tag.Scale == this.MapScale)
                        continue;

                    Debug.WriteLine($"Job Stopped [@StopUnnecessaryJobs]; tile: {currentJob.Tag.Tile?.ToShortString()} jobScale:{currentJob.Tag.Scale} expectedScale:{MapScale}");

                    currentJob.Operation.Abort();

                    this.jobs.Remove(currentJob);
                }
            }
        }

        public void ClearNonTiled()
        {
            for (int i = this.mapView.Children.Count - 1; i >= 0; i--)
            {
                var tag = ((LayerTag)((FrameworkElement)(this.mapView.Children[i])).Tag);

                if (tag.IsTiled)
                    continue;

                if (tag.LayerType == LayerType.BaseMap)
                    continue;

                this.mapView.Children.RemoveAt(i);
            }
        }

        public void ClearTiled()
        {
            for (int i = this.mapView.Children.Count - 1; i >= 0; i--)
            {
                var tag = ((LayerTag)((FrameworkElement)(this.mapView.Children[i])).Tag);

                if (!tag.IsTiled)
                    continue;

                this.mapView.Children.RemoveAt(i);
            }
        }

        public void ClearTiledButNotBaseMaps()
        {
            for (int i = this.mapView.Children.Count - 1; i >= 0; i--)
            {
                var tag = ((LayerTag)((FrameworkElement)(this.mapView.Children[i])).Tag);

                if (!tag.IsTiled || tag.LayerType == LayerType.BaseMap)
                    continue;

                this.mapView.Children.RemoveAt(i);
            }
        }

        public void ClearOutOfExtent(bool justTiled)
        {
            for (int i = this.mapView.Children.Count - 1; i >= 0; i--)
            {
                //Complex layer items may not be Path, so use FrameworkElement
                var tag = ((LayerTag)((FrameworkElement)(this.mapView.Children[i])).Tag);

                if (justTiled && !tag.IsTiled)
                    continue;

                if (this.CurrentTileInfos.Contains(tag.Tile) && tag.Scale == this.MapScale)
                    continue;

                if (tag.LayerType == LayerType.Raster && this.CurrentExtent.Intersects(tag.Layer.Extent))
                    continue;

                if (tag.LayerType == LayerType.ImagePyramid && this.CurrentExtent.Intersects(tag.Layer.Extent))
                    continue;

                this.mapView.Children.RemoveAt(i);
            }
        }

        public void ClearLayer(LayerType type, bool remove)
        {
            for (int i = this.mapView.Children.Count - 1; i >= 0; i--)
            {
                //Complex layer items may not be Path, so use FrameworkElement
                var tag = ((LayerTag)((FrameworkElement)(this.mapView.Children[i])).Tag);

                if (tag.LayerType.HasFlag(type))
                {
                    this.mapView.Children.RemoveAt(i);

                    if (remove)
                    {
                        this._layerManager.Remove(type);
                    }
                }
            }
        }

        public void ClearLayer(ILayer layer)
        {
            for (int i = this.mapView.Children.Count - 1; i >= 0; i--)
            {
                //Complex layer items may not be Path, so use FrameworkElement
                var tag = ((LayerTag)((FrameworkElement)(this.mapView.Children[i])).Tag);

                if (layer == tag.Layer)
                {
                    this.mapView.Children.RemoveAt(i);
                }
            }
        }

        public void RemoveEditableFeatureLayer(EditableFeatureLayer layer)
        {
            if (layer != null)
            {
                RemoveLayer((ILayer)layer);

                RemoveLayer((ILayer)layer.GetVertices());

                RemoveLayer((ILayer)layer.GetMidVertices());
            }
        }

        public void RemovePolyBezierLayer(PolyBezierLayer layer)
        {
            RemoveLayer(layer);

            RemoveLayer(layer.GetMainPointLayer());

            RemoveLayer(layer.GetControlPointLayer());

            if (layer.IsDecorated)
            {
                RemoveLayer(layer.GetDecorateLayer());
            }
        }

        public void RemoveLayer(ILayer layer)
        {
            for (int i = this.mapView.Children.Count - 1; i >= 0; i--)
            {
                //Complex layer items may not be Path, so use FrameworkElement
                var tag = ((LayerTag)((FrameworkElement)(this.mapView.Children[i])).Tag);

                if (layer == tag.Layer)
                {
                    this.mapView.Children.RemoveAt(i);
                }
            }

            this.Layers.Remove(layer);

            this._layerManager.Remove(layer);
        }

        public void ClearLayer(string layerName, bool remove)
        {
            for (int i = this.mapView.Children.Count - 1; i >= 0; i--)
            {
                var tag = ((LayerTag)((FrameworkElement)(this.mapView.Children[i])).Tag);

                if (tag.Layer?.LayerName == layerName)
                {
                    this.mapView.Children.RemoveAt(i);

                    if (remove)
                    {
                        this._layerManager.Remove(layerName);
                    }
                }
            }
        }

        public void RemovePolyBezierLayers()
        {
            for (int i = this.Layers.Count - 1; i >= 0; i--)
            {
                if (this.Layers[i] is PolyBezierLayer)
                {
                    RemovePolyBezierLayer(this.Layers[i] as PolyBezierLayer);
                }
            }
        }

        public void RemoveGeometries()
        {
            ClearLayer(LayerType.Drawing, true);
        }

        #endregion


        #region RightClick Options

        FrameworkElement rightClickOptions;

        sb.Primitives.ILocateable rightClickDataContext;

        public void RemoveRightClickOptions()
        {
            if (this.mapView.Children.Contains(rightClickOptions))
            {
                this.mapView.Children.Remove(rightClickOptions);
            }

            ClearLayer(LayerType.RightClickOption, true);
        }

        public void RegisterRightClickContextOptions<T>(sb.Primitives.ILocateable context) where T : FrameworkElement, new()
        {
            this.mapView.MouseUp -= mapView_MouseUp;
            this.mapView.MouseUp += mapView_MouseUp;

            this.rightClickOptions = new T();

            this.rightClickDataContext = context;

            this.rightClickOptions.DataContext = context;
        }

        public void RegisterRightClickContextOptions(FrameworkElement view, sb.Primitives.ILocateable context)
        {
            this.mapView.MouseUp -= mapView_MouseUp;
            this.mapView.MouseUp += mapView_MouseUp;

            this.rightClickOptions = view;

            this.rightClickDataContext = context;

            this.rightClickOptions.DataContext = context;
        }


        public void UnregisterRightClickContextOptions()
        {
            this.rightClickOptions = null;

            this.mapView.MouseUp -= mapView_MouseUp;
        }

        void mapView_MouseUp(object sender, MouseButtonEventArgs e)
        {
            //Do not raise when other options are available
            if (e.OriginalSource != this.mapView)
            {
                return;
            }

            RemoveRightClickOptions();

            if (e.ChangedButton != MouseButton.Right || itemIsMoving)
            {
                return;
            }

            var screenLocation = e.GetPosition(this.mapView);

            rightClickOptions = (FrameworkElement)Activator.CreateInstance(this.rightClickOptions.GetType());

            this.rightClickDataContext.Location = ScreenToMap(screenLocation).AsPoint();

            rightClickOptions.DataContext = this.rightClickDataContext;

            rightClickOptions.RenderTransformOrigin = new Point(rightClickOptions.Width / 2, -rightClickOptions.Height);

            var scaleTransform = ((TransformGroup)(rightClickOptions.RenderTransform)).Children.First();
            ((TransformGroup)(rightClickOptions.RenderTransform)).Children.Clear();

            ((TransformGroup)(rightClickOptions.RenderTransform)).Children.Add(scaleTransform);

            ((TransformGroup)(rightClickOptions.RenderTransform)).Children.Add(this.panTransformForPoints);

            ((TransformGroup)(rightClickOptions.RenderTransform)).Children.Add(
                new TranslateTransform(
                    screenLocation.X - rightClickOptions.Width / 2,
                    screenLocation.Y - rightClickOptions.Height / 2));

            Canvas.SetZIndex(rightClickOptions, int.MaxValue);

            rightClickOptions.Tag = new LayerTag(this.MapScale) { IsTiled = false, LayerType = LayerType.RightClickOption };

            this.mapView.Children.Add(rightClickOptions);
        }

        public void AddRightClickOptions(FrameworkElement options, MouseButtonEventArgs e, sb.Primitives.ILocateable context)
        {
            RemoveRightClickOptions();

            if (e.ChangedButton != MouseButton.Right || itemIsMoving)
            {
                return;
            }

            var screenLocation = e.GetPosition(this.mapView);

            var rightClickOptions = options;// (FrameworkElement)Activator.CreateInstance(options.GetType());

            context.Location = ScreenToMap(screenLocation).AsPoint();

            rightClickOptions.DataContext = context;

            rightClickOptions.RenderTransformOrigin = new Point(rightClickOptions.Width / 2, -rightClickOptions.Height);

            var scaleTransform = ((TransformGroup)(rightClickOptions.RenderTransform)).Children.First();
            ((TransformGroup)(rightClickOptions.RenderTransform)).Children.Clear();

            ((TransformGroup)(rightClickOptions.RenderTransform)).Children.Add(scaleTransform);

            ((TransformGroup)(rightClickOptions.RenderTransform)).Children.Add(this.panTransformForPoints);

            ((TransformGroup)(rightClickOptions.RenderTransform)).Children.Add(
                new TranslateTransform(
                    screenLocation.X - rightClickOptions.Width / 2,
                    screenLocation.Y - rightClickOptions.Height / 2));

            Canvas.SetZIndex(rightClickOptions, int.MaxValue);

            rightClickOptions.Tag = new LayerTag(this.MapScale) { IsTiled = false, LayerType = LayerType.RightClickOption };

            this.mapView.Children.Add(rightClickOptions);

            e.Handled = true;

        }

        #endregion



        #region Drawing & Anot

        public void Flash(List<Point> points)
        {
            //ClearAnimatingItems();
            ClearLayer(LayerType.AnimatingItem, false);

            if (points == null)
                return;

            foreach (Point item in points)
            {
                AddFlash(item);
            }
        }

        public void Flash(Point mapPoint)
        {
            //ClearAnimatingItems();
            ClearLayer(LayerType.AnimatingItem, false);

            AddFlash(mapPoint);
        }

        private void AddFlash(Point mapPoint)
        {
            Point point = this.panTransformForPoints.Inverse.Transform(this.viewTransform.Transform(mapPoint));

            EllipseGeometry geo = new EllipseGeometry(point, 8, 8);

            geo.Transform = this.panTransformForPoints;

            Path path = new Path()
            {
                Data = geo,
                Fill = new SolidColorBrush(Colors.Yellow),
                StrokeThickness = 2,
                Stroke = new SolidColorBrush(Colors.DarkGray),
                Tag = new LayerTag(MapScale) { LayerType = LayerType.AnimatingItem, IsTiled = false },// "anot",
                Opacity = .8
            };

            Point center = this.panTransformForPoints.Transform(point);

            ScaleTransform s = new ScaleTransform(1, 1, center.X, center.Y);

            path.RenderTransform = s;

            this.mapView.Children.Add(path);

            Canvas.SetZIndex(path, this.mapView.Children.Count);

            DoubleAnimation animation = new DoubleAnimation()
            {
                RepeatBehavior = new RepeatBehavior(5),
                AccelerationRatio = .2,
                DecelerationRatio = .8,
                FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop,
                From = 1,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(1000))
            };
            DoubleAnimation scaleAnimation = new DoubleAnimation()
            {
                RepeatBehavior = new RepeatBehavior(5),
                AccelerationRatio = .2,
                DecelerationRatio = .8,
                FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop,
                From = 1,
                To = 1.5,
                Duration = new Duration(TimeSpan.FromMilliseconds(1000))
            };

            path.BeginAnimation(Path.OpacityProperty, animation);
            s.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
            s.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
        }

        //Get the FontFamily in method parameters
        public void DrawGeometries(List<SqlGeometry> geometries, string layerName,
                                        VisualParameters visualElements, List<object> labels = null,
                                        Func<SqlGeometry, SqlGeometry> positionFunc = null, int fontSize = 0,
                                        Brush labelBackground = null, FontFamily font = null)
        {
            if (geometries == null || geometries.Count < 1)
                return;

            LabelParameters parameters = null;

            if (labels != null && positionFunc != null)
            {
                parameters = new LabelParameters(null, fontSize, labelBackground, new FontFamily("tahoma"), positionFunc);
            }

            var layer = new VectorLayer(
                layerName,
                new MemoryDataSource<object>(geometries, labels, i => i.ToString()),
                visualElements,
                LayerType.Drawing,
                RenderingApproach.Default,
                RasterizationApproach.GdiPlus,
                ScaleInterval.All,
                pointSymbol: null,
                labeling: parameters);

            this._layerManager.Add(layer);

            AddTiledLayer(layer);
        }

        public void DrawGeometries(List<SqlGeometry> geometries, string layerName,
                                        VisualParameters visualParameters, Geometry pointSymbol)
        {
            if (geometries == null || geometries.Count < 1)
                return;

            var layer = new VectorLayer(
                layerName,
                new MemoryDataSource<object>(geometries),
                visualParameters,
                LayerType.Drawing,
                RenderingApproach.Default,
                RasterizationApproach.DrawingVisual,
                ScaleInterval.All,
                pointSymbol);

            this._layerManager.Add(layer);

            AddNonTiledLayer(layer);
        }

        public void SelectGeometries(List<SqlGeometry> geometries, VisualParameters visualParameters, Geometry pointSymbol = null)
        {
            ClearLayer(LayerType.Selection, false);

            if (geometries == null || geometries.Count < 1)
                return;

            var layer = new VectorLayer(
                Guid.NewGuid().ToString(),
                new MemoryDataSource<object>(geometries),
                visualParameters,
                LayerType.Selection,
                RenderingApproach.Default,
                RasterizationApproach.DrawingVisual,
                ScaleInterval.All,
                pointSymbol);

            this._layerManager.Add(layer);

            AddNonTiledLayer(layer);
        }

        #endregion


        #region SelectPoint

        public void SelectPoint()
        {
            ResetMapViewEvents();

            this.SetCursor(Cursors.Cross);

            this.mapView.MouseUp -= new MouseButtonEventHandler(mapView_MouseUpForSelectPoint);

            this.mapView.MouseUp += new MouseButtonEventHandler(mapView_MouseUpForSelectPoint);
        }

        public void ReleaseSelectPoint()
        {
            this.SetCursor(Cursors.Arrow);

            this.mapView.MouseUp -= new MouseButtonEventHandler(mapView_MouseUpForSelectPoint);
        }

        private void mapView_MouseUpForSelectPoint(object sender, MouseButtonEventArgs e)
        {
            Debug.WriteLine(new StackTrace().GetFrame(0).GetMethod().Name, _eventEntered);

            Point point = Mouse.GetPosition(this.mapView);

            //if (this.OnPointSelected != null)
            //{
            //    this.OnPointSelected(null, new Common.PointEventArgs(ScreenToGeodetic(point)));
            //}
            this.OnPointSelected.SafeInvoke(null, new PointEventArgs(ScreenToGeodetic(point)));
        }

        private Task<sb.Point> SelectThePoint()
        {
            selectPointCancelationToken = new CancellationTokenSource();

            var tcs = new TaskCompletionSource<sb.Point>();

            ResetMapViewEvents();

            this.SetCursor(Cursors.Cross);

            MouseButtonEventHandler action = null;

            action = (sender, e) =>
              {
                  this.mapView.MouseUp -= action;

                  this.SetCursor(Cursors.Arrow);

                  tcs.SetResult(ScreenToGeodetic(Mouse.GetPosition(this.mapView)).AsPoint());
              };

            this.mapView.MouseUp -= action;

            this.mapView.MouseUp += action;

            selectPointCancelationToken.Token.Register(() =>
            {
                tcs.TrySetCanceled();

                this.SetCursor(Cursors.Arrow);

                tcs = null;

            }, useSynchronizationContext: false);


            return tcs.Task;
        }

        public async Task<sb.Point> SelectPointAsync()
        {
            try
            {
                if (selectPointCancelationToken != null)
                {
                    selectPointCancelationToken.Cancel();
                }

                var result = await SelectThePoint();

                return result;
            }
            catch (TaskCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        CancellationTokenSource selectPointCancelationToken;

        #endregion



        #region Pan

        Point prevMouseLocation;

        //POSSIBLY EXTRA FIELD
        Point startPointLocationForPan;

        //bool isPanEnabled = false;

        public void Pan()
        {
            //this.isPanEnabled = true;

            ResetMapViewEvents();

            this.mapView.MouseDown -= mapView_MouseDownForPan;
            this.mapView.MouseDown += mapView_MouseDownForPan;

            this.CurrentMouseAction = MapAction.Pan;

            //this.mapView.MouseUp -= mapView_MouseUpForPan;
            //this.mapView.MouseUp += mapView_MouseUpForPan;
        }

        public void ReleasePan()
        {
            //this.isPanEnabled = false;
            this.CurrentMouseAction = MapAction.None;

            ResetMapViewEvents();

            this.mapView.MouseDown -= mapView_MouseDownForPan;
        }

        private void mapView_MouseDownForPan(object sender, MouseButtonEventArgs e)
        {
            Debug.WriteLine(new StackTrace().GetFrame(0).GetMethod().Name, _eventEntered);

            if (this.viewTransform == null || itemIsMoving)
            {
                Debug.WriteLine(new StackTrace().GetFrame(0).GetMethod().Name, _eventEscaped);

                return;
            }

            //Abort();

            Mouse.Capture(this.mapView);

            //ClearAnimatingItems();
            ClearLayer(LayerType.AnimatingItem, false);

            ClearLayer(LayerType.RightClickOption, false);

            this.prevMouseLocation = e.GetPosition(this.mapView);

            this.startPointLocationForPan = this.prevMouseLocation;

            this.MouseMove -= mapView_MouseMoveForPan;
            this.MouseMove += mapView_MouseMoveForPan;

            this.MouseUp -= mapView_MouseUpForPan;
            this.MouseUp += mapView_MouseUpForPan;
        }

        private void mapView_MouseUpForPan(object sender, MouseButtonEventArgs e)
        {
            //Debug.WriteLine(new StackTrace().GetFrame(0).GetMethod().Name, _eventEntered);

            this.MouseMove -= mapView_MouseMoveForPan;

            this.MouseUp -= mapView_MouseUpForPan;

            //Abort();

            this.mapView.ReleaseMouseCapture();

            Point currentMouseLocation = e.GetPosition(this.mapView);

            double xOffset = currentMouseLocation.X - this.startPointLocationForPan.X;

            double yOffset = currentMouseLocation.Y - this.startPointLocationForPan.Y;

            if (Math.Abs(xOffset) > 2 || Math.Abs(yOffset) > 2)
            {
                Debug.WriteLine(new StackTrace().GetFrame(0).GetMethod().Name, _refreshCalled);

                Refresh();
            }
            else
            {
                Debug.WriteLine(new StackTrace().GetFrame(0).GetMethod().Name, _methodEscaped);
            }
        }

        private void mapView_MouseMoveForPan(object sender, MouseEventArgs e)
        {
            Point currentMouseLocation = e.GetPosition(this.mapView);

            double xOffset = currentMouseLocation.X - this.prevMouseLocation.X;

            double yOffset = currentMouseLocation.Y - this.prevMouseLocation.Y;

            if (Math.Abs(xOffset) > 2 || Math.Abs(yOffset) > 2)
            {
                this.panTransform.X += xOffset * 1.0 / this.zoomTransform.ScaleX;

                this.panTransform.Y += yOffset * 1.0 / this.zoomTransform.ScaleY;

                this.prevMouseLocation = currentMouseLocation;

                this.panTransformForPoints.X += xOffset;

                this.panTransformForPoints.Y += yOffset;

                UpdateTileInfos();
            }

        }

        private void ExtentManager_OnTilesRemoved(object sender, CustomEventArgs<List<TileInfo>> e)
        {
            Debug.Print($"ExtentManager_OnTilesRemoved: {string.Join(" # ", e.Arg.Select(i => i.ToShortString()))}");

            lock (locker)
            {
                for (int i = jobs.Count - 1; i >= 0; i--)
                {
                    var currentJob = jobs[i];

                    if (currentJob == null)
                    {
                        this.jobs.Remove(currentJob);

                        continue;
                    }

                    //if (currentJob != null && currentJob.Operation != null)
                    //{
                    if (e.Arg.Contains(currentJob.Tag.Tile) && !this.CurrentTileInfos.Contains(currentJob.Tag.Tile))
                    {
                        Debug.Print($"Job Stopped [@TileRemoved] {currentJob.Tag.Tile?.ToShortString()}");

                        currentJob.Operation.Abort();

                        this.jobs.Remove(currentJob);
                    }
                    //}
                }
            }

            ClearOutOfExtent(true);
        }

        private void ExtentManager_OnTilesAdded(object sender, CustomEventArgs<List<TileInfo>> e)
        {
            Debug.Print($"ExtentManager_OnTilesAdded: {string.Join(" # ", e.Arg.Select(i => i.ToShortString()))}");

            foreach (var item in e.Arg)
            {
                if (item.ZoomLevel != this.CurrentZoomLevel)
                {
                    Debug.Print($"Zoom Level Conflict [@ExtentManager_OnTilesAdded] {item.ToShortString()} ; expected zoom level: {this.CurrentZoomLevel}");
                    return;
                }


                if (!CurrentTileInfos.Contains(item))
                {
                    Debug.Print($"Not in `CurrentTileInfos` [@ExtentManager_OnTilesAdded] {item.ToShortString()} ; expected zoom level: {this.CurrentZoomLevel}");
                    continue;
                }

                RefreshTiles(item);

            }

        }

        Path GetTileBorder(TileInfo tile, bool isNew, bool isOld, bool isDefault)
        {
            var p1 = tile.MercatorExtent.TopLeft.AsWpfPoint();

            var p2 = tile.MercatorExtent.BottomRight.AsWpfPoint();

            RectangleGeometry geometry = new RectangleGeometry(new Rect(p1, p2), 0, 0);

            geometry.Transform = viewTransform;

            SolidColorBrush stroke;

            if (isNew)
            {
                stroke = new SolidColorBrush(Colors.Green);
            }
            else if (isOld)
            {
                stroke = new SolidColorBrush(Colors.Red);
            }
            else if (isDefault)
            {
                stroke = new SolidColorBrush(Colors.Gray);
            }
            else
            {
                throw new NotImplementedException();
            }

            Path path = new Path()
            {
                Data = geometry,
                Fill = new SolidColorBrush(Colors.Transparent),
                StrokeThickness = 1,
                Stroke = stroke,
                Tag = new LayerTag(this.MapScale) { Tile = tile, IsTiled = true, IsNew = true, LayerType = LayerType.GridAndGraticule }

            };

            return path;
        }

        //Consider removeing checking Tag is LayerTag, All childrens should have a LayerTag as Tag
        private List<Path> Find(TileInfo tile)
        {
            List<Path> result = new List<Path>();

            foreach (var item in mapView.Children)
            {
                if (item is Path)
                {
                    if ((item as Path).Tag is LayerTag)
                    {
                        if (tile.Equals(((item as Path).Tag as LayerTag).Tile))
                        {
                            result.Add(item as Path);
                        }
                    }
                }
            }

            return result;
        }

        private void ClearTileBorder(TileInfo tile)
        {
            for (int i = this.mapView.Children.Count - 1; i >= 0; i--)
            {
                Shape temp = this.mapView.Children[i] as Shape;

                if (temp != null)
                {
                    var tag = temp.Tag as LayerTag;

                    if (tag != null && tag.LayerType != LayerType.BaseMap)
                    {
                        if (tag.Tile.Equals(tile))
                        {
                            this.mapView.Children.RemoveAt(i);
                        }
                    }
                }
            }
        }

        //It has animation
        public void Pan(double xOffset, double yOffset, Action callback = null)
        {

            //StopUnnecessaryJobs();

            //ClearAnimatingItems();
            ClearLayer(LayerType.AnimatingItem, false);

            counterValue = 4;
            counter = 0;

            if (Math.Abs(xOffset) > 2 || Math.Abs(yOffset) > 2)
            {

                DoubleAnimation animation = new DoubleAnimation()
                {
                    Duration = new Duration(TimeSpan.FromMilliseconds(100)),
                    FillBehavior = FillBehavior.Stop
                };

                animation.Completed += new EventHandler(
                    delegate (object o, EventArgs e)
                    {
                        Debug.WriteLine(new StackTrace().GetFrame(0).GetMethod().Name + "animation called");

                        if (++counter != counterValue)
                            return;

                        Debug.WriteLine(new StackTrace().GetFrame(0).GetMethod().Name + "animation completed", _refreshCalled);

                        UpdateTileInfos();

                        Refresh();

                        if (callback != null)
                        {
                            Dispatcher.BeginInvoke(callback, System.Windows.Threading.DispatcherPriority.Background, null);
                        }

                        this.counterValue = -1;
                    });

                animation.To = this.panTransform.X + xOffset * 1.0 / this.zoomTransform.ScaleX;
                this.panTransform.BeginAnimation(TranslateTransform.XProperty, animation);

                animation.To = this.panTransform.Y + yOffset * 1.0 / this.zoomTransform.ScaleY;
                this.panTransform.BeginAnimation(TranslateTransform.YProperty, animation);

                animation.To = this.panTransformForPoints.X + xOffset;
                this.panTransformForPoints.BeginAnimation(TranslateTransform.XProperty, animation);

                animation.To = this.panTransformForPoints.Y + yOffset;
                this.panTransformForPoints.BeginAnimation(TranslateTransform.YProperty, animation);

                this.panTransform.X += xOffset * 1.0 / this.zoomTransform.ScaleX;
                this.panTransform.Y += yOffset * 1.0 / this.zoomTransform.ScaleY;
                this.panTransformForPoints.X = this.panTransformForPoints.X + xOffset;
                this.panTransformForPoints.Y = this.panTransformForPoints.Y + yOffset;
            }
            else
            {
                Debug.WriteLine(new StackTrace().GetFrame(0).GetMethod().Name, _methodEscaped);
            }
        }

        public void PanTo(double x, double y, Action callback)
        {
            double screenY = this.mapView.ActualHeight / 2.0;

            double screenX = this.mapView.ActualWidth / 2.0;

            Point center = MapToScreen(new Point(x, y));

            this.Pan(screenX - center.X, screenY - center.Y, callback);
        }

        #endregion


        #region Zoom

        Point firstZoomBound;

        int counter; int counterValue;

        //POTENTIALLY ERROR PROUNE; Tag value should not be `string`
        Rectangle rectangle = new Rectangle()
        {
            Stroke = new SolidColorBrush(new Color() { R = 255, G = 200, B = 0, A = 255 }),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(new Color() { R = 255, G = 240, B = 0, A = 40 }),
            Tag = new LayerTag(-1) { IsTiled = false, LayerType = LayerType.Drawing }
        };

        public void FullExtent()
        {
            ZoomToExtent(this._layerManager.CalculateMapExtent(), false);
        }

        public void ZoomIn()
        {
            ResetMapViewEvents();

            this.mapView.MouseDown -= mapView_MouseDownForZoom;
            this.mapView.MouseDown += mapView_MouseDownForZoom;

            this.CurrentMouseAction = MapAction.ZoomInRectangle;
        }

        //public void ZoomOut()
        //{
        //    ResetMapViewEvents();

        //    this.mapView.MouseDown -= mapView_MouseDownForZoom;
        //    this.mapView.MouseDown += mapView_MouseDownForZoom;

        //    this.CurrentMouseAction = MapAction.ZoomOutRectangle;
        //}

        public void ZoomOutPoint()
        {
            ResetMapViewEvents();

            this.mapView.MouseUp -= mapView_MouseDownForZoomOut;
            this.mapView.MouseUp += mapView_MouseDownForZoomOut;

            this.CurrentMouseAction = MapAction.ZoomOut;
        }

        private void mapView_MouseDownForZoom(object sender, MouseButtonEventArgs e)
        {
            Debug.WriteLine(new StackTrace().GetFrame(0).GetMethod().Name, _eventEntered);

            if (this.viewTransform == null)
            //if (this.mapTransform == null)
            {
                return;
            }

            //StopUnnecessaryJobs();

            Mouse.Capture(this.mapView);

            this.firstZoomBound = e.GetPosition(this.mapView);

            rectangle.Width = 0; rectangle.Height = 0;

            rectangle.Fill.Freeze();

            this.mapView.Children.Add(rectangle);

            Canvas.SetZIndex(rectangle, this.mapView.Children.Count);

            this.mapView.MouseUp -= mapView_MouseUpForZoom;
            this.mapView.MouseUp += mapView_MouseUpForZoom;

            this.mapView.MouseMove -= mapView_MouseMoveForZoom;
            this.mapView.MouseMove += mapView_MouseMoveForZoom;
        }

        private void mapView_MouseMoveForZoom(object sender, MouseEventArgs e)
        {
            Point currMouseLocation = e.GetPosition(this.mapView);

            double xOffset = currMouseLocation.X - this.prevMouseLocation.X;

            double yOffset = currMouseLocation.Y - this.prevMouseLocation.Y;

            Rect rect = new Rect(this.firstZoomBound, currMouseLocation);

            rectangle.Width = rect.Width; rectangle.Height = rect.Height;

            Canvas.SetTop(rectangle, rect.Top);

            Canvas.SetLeft(rectangle, rect.Left);
        }

        private void mapView_MouseUpForZoom(object sender, MouseButtonEventArgs e)
        {
            Debug.WriteLine(new StackTrace().GetFrame(0).GetMethod().Name, _eventEntered);

            this.mapView.MouseUp -= mapView_MouseUpForZoom;
            this.mapView.MouseMove -= mapView_MouseMoveForZoom;

            this.mapView.Children.Remove(rectangle);

            Point currMouseLocation = e.GetPosition(this.mapView);

            //Rect rect = new Rect(firstZoomBound, currMouseLocation);
            Rect rect = new Rect(ScreenToMap(firstZoomBound), ScreenToMap(currMouseLocation));

            //if (this.CurrentMouseAction == MapAction.ZoomOutRectangle)
            //{
            //    rect.Scale(1.2, 1.2);
            //}

            this.mapView.ReleaseMouseCapture();

            ZoomToExtent(rect, true);
        }

        private void mapView_MouseDownForZoomOut(object sender, MouseButtonEventArgs e)
        {
            Point canvasPosition = e.GetPosition(this.mapView);

            ZoomToPoint(canvasPosition, .75);
        }



        public void ZoomToExtent(sb.BoundingBox boundingBox)
        {
            ZoomToExtent(boundingBox, false, true);
        }

        public void ZoomToExtent(sb.BoundingBox boundingBox, bool canChangeToPointZoom, bool isExactExtent = true, Action callback = null, bool withAnimation = true)
        {
            var layerExtent = new Rect(boundingBox.TopLeft.AsWpfPoint(), boundingBox.BottomRight.AsWpfPoint());
            //Rect layerExtent = MapExtentToIntermediateExtent(boundingBox);

            ZoomToExtent(layerExtent, canChangeToPointZoom, isExactExtent, callback, withAnimation);
        }

        //It has animation
        private async void ZoomToExtent(Rect mapBoundingBox, bool canChangeToPointZoom, bool isExactExtent = true, Action callback = null, bool withAnimation = true)
        {
            if (double.IsNaN(mapBoundingBox.Width + mapBoundingBox.Height))
            {
                return;
            }
            else if (mapBoundingBox.Width + mapBoundingBox.Height == 0)
            {
                //PanTo(mapBoundingBox.X, mapBoundingBox.Y, callback);

                int newZoomLevel = WebMercatorUtility.GetNextZoomLevel(CurrentZoomLevel);

                this.Zoom(WebMercatorUtility.GetGoogleMapScale(newZoomLevel), MapToScreen(mapBoundingBox.Location), callback);

                return;
            }

            //StopUnnecessaryJobs();

            ClearLayer(LayerType.AnimatingItem, false);

            ClearLayer(LayerType.Complex, false);

            ClearLayer(LayerType.MoveableItem, false);

            ClearLayer(LayerType.EditableItem, false);

            counter = 0;

            Point lowerLeft = (mapBoundingBox.BottomLeft);

            Point upperRight = (mapBoundingBox.TopRight);

            if ((lowerLeft - upperRight).Length < 15 && canChangeToPointZoom)
            {
                ZoomToPoint(MapToScreen(mapBoundingBox.Location), 1.25);
            }
            else
            {
                counterValue = 8;

                Point intermediateExtentCenter = new Point((mapBoundingBox.Left + mapBoundingBox.Right) / 2.0,
                                                            (mapBoundingBox.Top + mapBoundingBox.Bottom) / 2.0);

                Point windowCenter = new Point(this.mapView.ActualWidth / 2.0, this.mapView.ActualHeight / 2.0);

                Point screenExtentCenter = MapToScreen(intermediateExtentCenter);

                double xScale = (isExactExtent ? this.mapView.ActualWidth : this.mapView.ActualWidth - 20) / mapBoundingBox.Width;

                double yScale = (isExactExtent ? this.mapView.ActualHeight : this.mapView.ActualHeight - 20) / mapBoundingBox.Height;

                double scale = xScale > yScale ? yScale : xScale;

                double pointScale = ToMapScale(scale) / this.MapScale;

                if (withAnimation)
                {

                    var duration = new Duration(TimeSpan.FromMilliseconds(100));

                    var fillBehavior = FillBehavior.Stop;

                    DoubleAnimation animationPanX = new DoubleAnimation(windowCenter.X - intermediateExtentCenter.X, duration, fillBehavior);
                    var t1 = AnimateAsync(() => { this.panTransform.BeginAnimation(TranslateTransform.XProperty, animationPanX); }, animationPanX);

                    DoubleAnimation animationPanY = new DoubleAnimation(windowCenter.Y - intermediateExtentCenter.Y, duration, fillBehavior);
                    var t2 = AnimateAsync(() => { this.panTransform.BeginAnimation(TranslateTransform.YProperty, animationPanY); }, animationPanY);

                    DoubleAnimation animationPanPX = new DoubleAnimation(windowCenter.X - screenExtentCenter.X, duration, fillBehavior);
                    var t3 = AnimateAsync(() => { this.panTransformForPoints.BeginAnimation(TranslateTransform.XProperty, animationPanPX); }, animationPanPX);

                    DoubleAnimation animationPanPY = new DoubleAnimation(windowCenter.Y - screenExtentCenter.Y, duration, fillBehavior);
                    var t4 = AnimateAsync(() => { this.panTransformForPoints.BeginAnimation(TranslateTransform.YProperty, animationPanPY); }, animationPanPY);


                    DoubleAnimation animationZoomX = new DoubleAnimation(this.mapView.ActualWidth / 2.0, duration, fillBehavior);
                    var t5 = AnimateAsync(() => { this.zoomTransform.BeginAnimation(ScaleTransform.CenterXProperty, animationZoomX); }, animationZoomX);

                    DoubleAnimation animationZoomY = new DoubleAnimation(this.mapView.ActualHeight / 2.0, duration, fillBehavior);
                    var t6 = AnimateAsync(() => { this.zoomTransform.BeginAnimation(ScaleTransform.CenterYProperty, animationZoomY); }, animationZoomY);

                    DoubleAnimation animationZoomSX = new DoubleAnimation(scale * baseScaleX, duration, fillBehavior);
                    var t7 = AnimateAsync(() => { this.zoomTransform.BeginAnimation(ScaleTransform.ScaleXProperty, animationZoomSX); }, animationZoomSX);

                    DoubleAnimation animationZoomSY = new DoubleAnimation(scale * baseScaleY, duration, fillBehavior);
                    var t8 = AnimateAsync(() => { this.zoomTransform.BeginAnimation(ScaleTransform.ScaleYProperty, animationZoomSY); }, animationZoomSY);

                    await Task.WhenAll(t1, t2, t3, t4, t5, t6, t7, t8);
                }

                this.panTransform.X = windowCenter.X - intermediateExtentCenter.X;
                this.panTransform.Y = windowCenter.Y - intermediateExtentCenter.Y;

                this.panTransformForPoints.X = 0;
                this.panTransformForPoints.Y = 0;

                this.zoomTransform.CenterX = this.mapView.ActualWidth / 2.0;

                this.zoomTransform.CenterY = this.mapView.ActualHeight / 2.0;

                this.zoomTransform.ScaleX = scale * baseScaleX;

                this.zoomTransform.ScaleY = scale * baseScaleY;

                this._theScreenScale = scale * baseScaleX;

                this.OnZoomChanged.SafeInvoke(null, null);

                Refresh();

                if (callback != null)
                {
                    await Dispatcher.BeginInvoke(callback, DispatcherPriority.Background, null);
                }

            }
        }

        private Task AnimateAsync(Action action, DoubleAnimation animation)
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();

            animation.Completed += (s, e) => { tcs.SetResult(true); };

            action();

            return tcs.Task;
        }

        ////It has animation
        //private void ZoomToExtent(Rect intermediateBoundingBox, bool ignoreCase, bool isExactExtent = true, Action callback = null)
        //{
        //    if (double.IsNaN(intermediateBoundingBox.Width + intermediateBoundingBox.Height))
        //        return;

        //    else if (intermediateBoundingBox.Width + intermediateBoundingBox.Height == 0)
        //    {
        //        PanTo(intermediateBoundingBox.X, intermediateBoundingBox.Y, callback);

        //        return;
        //    }

        //    StopUnnecessaryJobs();

        //    ClearLayer(LayerType.AnimatingItem, false);

        //    ClearLayer(LayerType.Complex, false);


        //    counter = 0;

        //    Point lowerLeft = (intermediateBoundingBox.BottomLeft);

        //    Point upperRight = (intermediateBoundingBox.TopRight);

        //    if ((lowerLeft - upperRight).Length < 15 && !ignoreCase)
        //    {
        //        ZoomToPoint(MapToScreen(intermediateBoundingBox.Location), 1.25);
        //    }
        //    else
        //    {
        //        counterValue = 8;

        //        Point intermediateExtentCenter = new Point((intermediateBoundingBox.Left + intermediateBoundingBox.Right) / 2.0,
        //                                                    (intermediateBoundingBox.Top + intermediateBoundingBox.Bottom) / 2.0);

        //        Point windowCenter = new Point(this.mapView.ActualWidth / 2.0, this.mapView.ActualHeight / 2.0);

        //        Point screenExtentCenter = MapToScreen(intermediateExtentCenter);

        //        double xScale = (isExactExtent ? this.mapView.ActualWidth : this.mapView.ActualWidth - 20) / intermediateBoundingBox.Width;

        //        double yScale = (isExactExtent ? this.mapView.ActualHeight : this.mapView.ActualHeight - 20) / intermediateBoundingBox.Height;

        //        double scale = xScale > yScale ? yScale : xScale;

        //        double pointScale = ToMapScale(scale) / this.MapScale;

        //        DoubleAnimation animation = new DoubleAnimation()
        //        {
        //            Duration = new Duration(TimeSpan.FromMilliseconds(100)),
        //            FillBehavior = FillBehavior.Stop,
        //        };

        //        animation.Completed += (s, e) =>
        //        {
        //            if (++counter != counterValue)
        //                return;

        //            this.OnZoomChanged.SafeInvoke(null, null);

        //            Refresh();

        //            if (callback != null)
        //            {
        //                Dispatcher.BeginInvoke(callback, DispatcherPriority.Background, null);
        //            }

        //            //RaisePropertyChanged("MapScale");

        //            this.counterValue = -1;
        //        };

        //        animation.To = windowCenter.X - intermediateExtentCenter.X;
        //        this.panTransform.BeginAnimation(TranslateTransform.XProperty, animation);

        //        animation.To = windowCenter.Y - intermediateExtentCenter.Y;
        //        this.panTransform.BeginAnimation(TranslateTransform.YProperty, animation);

        //        animation.To = windowCenter.X - screenExtentCenter.X;
        //        this.panTransformForPoints.BeginAnimation(TranslateTransform.XProperty, animation);

        //        animation.To = windowCenter.Y - screenExtentCenter.Y;
        //        this.panTransformForPoints.BeginAnimation(TranslateTransform.YProperty, animation);

        //        this.panTransform.X = windowCenter.X - intermediateExtentCenter.X;
        //        this.panTransform.Y = windowCenter.Y - intermediateExtentCenter.Y;

        //        this.panTransformForPoints.X = 0;
        //        this.panTransformForPoints.Y = 0;

        //        animation.To = this.mapView.ActualWidth / 2.0;
        //        this.zoomTransform.BeginAnimation(ScaleTransform.CenterXProperty, animation);

        //        animation.To = this.mapView.ActualHeight / 2.0;
        //        this.zoomTransform.BeginAnimation(ScaleTransform.CenterYProperty, animation);

        //        animation.To = scale * baseScaleX;
        //        this.zoomTransform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);

        //        animation.To = scale * baseScaleY;
        //        this.zoomTransform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);

        //        this.zoomTransform.CenterX = this.mapView.ActualWidth / 2.0;

        //        this.zoomTransform.CenterY = this.mapView.ActualHeight / 2.0;

        //        this.zoomTransform.ScaleX = scale * baseScaleX;

        //        this.zoomTransform.ScaleY = scale * baseScaleY;
        //    }
        //}

        private void ZoomToPoint(Point windowPoint, double deltaZoom)
        {
            Debug.Print("ZoomToPoint(Point windowPoint, double deltaZoom)");
            //StopUnnecessaryJobs();

            Point layerPoint = this.viewTransform.Inverse.Transform(windowPoint);

            this.panTransform.X = this.mapView.ActualWidth / 2.0 - layerPoint.X;

            this.panTransform.Y = this.mapView.ActualHeight / 2.0 - layerPoint.Y;

            //94.09.24: zoomTranform.ScaleX value may be at an animation!
            //double scale = this.zoomTransform.ScaleX * deltaZoom;
            double scale = this._theScreenScale * deltaZoom;
            //
            this.zoomTransform.CenterX = this.mapView.ActualWidth / 2.0;

            this.zoomTransform.CenterY = this.mapView.ActualHeight / 2.0;

            this.zoomTransform.ScaleX = scale * baseScaleX;

            this.zoomTransform.ScaleY = scale * baseScaleY;

            this._theScreenScale = scale * baseScaleX;

            Debug.WriteLine(new StackTrace().GetFrame(0).GetMethod().Name, _refreshCalled);

            //if (this.OnZoomChanged != null)
            //{
            //    this.OnZoomChanged(null, null);
            //}
            this.OnZoomChanged.SafeInvoke(null, null);

            Refresh();

            //RaisePropertyChanged("MapScale");
        }

        public void Zoom(double mapScale)
        {
            Debug.Print("Zoom(double mapScale) is called");

            this.Zoom(mapScale, new Point(this.mapView.ActualWidth / 2.0, this.mapView.ActualHeight / 2.0));
        }

        public void Zoom(int googleZoomLevel)
        {
            if (googleZoomLevel < 1 || googleZoomLevel > 23)
            {
                return;
            }

            Zoom(WebMercatorUtility.GetGoogleMapScale(googleZoomLevel));
        }

        //public void Zoom(double mapScale, sb.Point mapPoint)
        //{
        //    var point = MapToScreen(mapPoint.AsWpfPoint());

        //    Zoom(mapScale, mapPoint);
        //}

        public void Zoom(double mapScale, Point windowCenter, Action callback = null)
        {
            double scale = ToScreenScale(mapScale);

            double leftX = windowCenter.X / scale;

            double rightX = (this.mapView.ActualWidth - windowCenter.X) / scale;

            double topY = windowCenter.Y / scale;

            double bottomY = (this.mapView.ActualHeight - windowCenter.Y) / scale;

            Point mapCenter = ScreenToMap(windowCenter);

            sb.BoundingBox boundingBox =
                new sb.BoundingBox(mapCenter.X - leftX, mapCenter.Y + topY, mapCenter.X + rightX, mapCenter.Y - bottomY);

            ZoomToExtent(boundingBox, false, true, callback);
        }

        private void ZoomAndCenter(double mapScale, Point windowCenter, Action callback = null, bool withAnimation = true)
        {
            double scale = ToScreenScale(mapScale);

            double width = this.mapView.ActualWidth / scale;

            double height = this.mapView.ActualHeight / scale;

            Point mapCenter = ScreenToMap(windowCenter);

            sb.BoundingBox boundingBox =
                new sb.BoundingBox(mapCenter.X - width / 2.0, mapCenter.Y - height / 2.0, mapCenter.X + width / 2.0, mapCenter.Y + height / 2.0);

            ZoomToExtent(boundingBox, false, true, callback, withAnimation);
        }

        public void ZoomAndCenter(double mapScale, sb.Point centerMapPoint, Action callback = null, bool withAnimation = true)
        {
            var windowCenter = MapToScreen(new Point(centerMapPoint.X, centerMapPoint.Y));

            ZoomAndCenter(mapScale, windowCenter, callback, withAnimation);
        }

        public void ZoomToLevelAndCenter(int zoomLevel, sb.Point centerMapPoint, Action callback = null, bool withAnimation = true)
        {
            var mapScale = WebMercatorUtility.GetGoogleMapScale(zoomLevel);

            ZoomAndCenter(mapScale, centerMapPoint, callback, withAnimation);
        }

        public void Zoom(double mapScale, sb.Point centerMapPoint, Action callback = null)
        {
            var windowCenter = MapToScreen(new Point(centerMapPoint.X, centerMapPoint.Y));

            ZoomAndCenter(mapScale, windowCenter, callback);
        }

        private void mapView_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            Debug.WriteLine(new StackTrace().GetFrame(0).GetMethod().Name, _eventEntered);

            Debug.Print($"e.Delta: {e.Delta}");

            //StopUnnecessaryJobs();

            Point point = e.GetPosition(this.mapView);

            if (IsGoogleZoomLevelsEnabled)
            {
                int newZoomLevel = e.Delta > 0 ? WebMercatorUtility.GetNextZoomLevel(CurrentZoomLevel) : WebMercatorUtility.GetPreviousZoomLevel(CurrentZoomLevel);

                this.Zoom(WebMercatorUtility.GetGoogleMapScale(newZoomLevel), point);
            }
            else
            {
                double delta = e.Delta > 0 ? 1.5 : 0.5;

                //this.ZoomToPoint(point, delta);
                //double newMapScale = ToMapScale(this.zoomTransform.ScaleX * delta);
                double newMapScale = ToMapScale(this.ScreenScale * delta);

                this.Zoom(newMapScale, point);
            }

            Debug.WriteLine(new StackTrace().GetFrame(0).GetMethod().Name, _eventLeaved);

        }


        private void ZoomToFeature(SqlGeometry geometry)
        {
            if (geometry == null || geometry.IsNull)
            {
                return;
            }

            if (geometry.GetOpenGisType() == OpenGisGeometryType.Point)
            {
                this.Zoom(WebMercatorUtility.GetGoogleMapScale(13), geometry.AsPoint());
            }
            else
            {
                this.ZoomToExtent(geometry.GetBoundingBox());
            }

        }

        public void EnableZoomingOnMouseWheel()
        {
            this.mapView.MouseWheel -= mapView_MouseWheel;
            this.mapView.MouseWheel += mapView_MouseWheel;
        }

        public void DisableZoomingOnMouseWheel()
        {
            this.mapView.MouseWheel -= mapView_MouseWheel;
        }

        #endregion


        #region Draw Shapes

        bool displayDrawingPath = false;

        DrawingLayer drawingLayer;

        DrawMode drawMode;

        bool itWasPanningWhileDrawing { get; set; }

        CancellationTokenSource drawingCancellationToken;

        TaskCompletionSource<sb.Primitives.Geometry> taskCompletionSource;

        private void MapView_MouseDownForStartDrawing(object sender, MouseButtonEventArgs e)
        {
            Debug.WriteLine("START");

            if (e.ChangedButton != MouseButton.Left)
                return;

            this.mapView.MouseDown -= MapView_MouseDownForStartDrawing;

            this.prevMouseLocation = (e.GetPosition(this.mapView));

            var mercatorPoint = ScreenToMap(this.prevMouseLocation).AsPoint();

            this.RemoveLayer(drawingLayer);

            this.drawingLayer = new DrawingLayer(this.drawMode, this.viewTransform, mercatorPoint);

            this.drawingLayer.OnRequestFinishDrawing += (s, arg) =>
            {
                FinishDrawing();
            };

            this.SetLayer(drawingLayer);

            this.AddEditableFeatureLayer(drawingLayer.GetLayer());

            if (this.drawMode == DrawMode.Point)
            {
                FinishDrawing();
            }
            else
            {
                //this.mapView.CaptureMouse();

                this.mapView.MouseMove -= MapView_MouseMoveDrawing;
                this.mapView.MouseMove += MapView_MouseMoveDrawing;

                this.mapView.MouseDown -= MapView_MouseDownForPanWhileDrawing;
                this.mapView.MouseDown += MapView_MouseDownForPanWhileDrawing;

                this.mapView.MouseUp -= MapView_MouseUpForDrawing;
                this.mapView.MouseUp += MapView_MouseUpForDrawing;
            }
        }

        private void FinishDrawing()
        {
            this.mapView.MouseUp -= MapView_MouseUpForDrawing;
            this.mapView.MouseDown -= MapView_MouseDownForStartDrawing;
            this.mapView.MouseMove -= MapView_MouseMoveDrawing;
            this.mapView.MouseDown -= MapView_MouseDownForPanWhileDrawing;

            ResetMapViewEvents();

            this.RemoveLayer(drawingLayer);

            this.RemoveEditableFeatureLayer(drawingLayer.GetLayer());

            taskCompletionSource.SetResult(drawingLayer.GetFinalGeometry());
        }

        private void MapView_MouseMoveDrawing(object sender, MouseEventArgs e)
        {
            Point currentMouseLocation = (e.GetPosition(this.mapView));

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                double xOffset = currentMouseLocation.X - this.prevMouseLocation.X;

                double yOffset = currentMouseLocation.Y - this.prevMouseLocation.Y;

                if (Math.Abs(xOffset) > 0 || Math.Abs(yOffset) > 0)
                {
                    this.panTransform.X += xOffset * 1.0 / this.zoomTransform.ScaleX;

                    this.panTransform.Y += yOffset * 1.0 / this.zoomTransform.ScaleY;

                    this.prevMouseLocation = currentMouseLocation;

                    this.panTransformForPoints.X += xOffset;

                    this.panTransformForPoints.Y += yOffset;

                    UpdateTileInfos();

                    this.itWasPanningWhileDrawing = true;
                }
                else
                {

                }

            }
            else
            {
                this.drawingLayer.UpdateLastVertexLocation(ScreenToMap(currentMouseLocation).AsPoint());
            }
        }

        private void MapView_MouseDownForPanWhileDrawing(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
                return;

            Debug.WriteLine("DOWND");

            e.Handled = true;

            itWasPanningWhileDrawing = false;

            this.prevMouseLocation = (e.GetPosition(this.mapView));

        }

        private void MapView_MouseUpForDrawing(object sender, MouseButtonEventArgs e)
        {
            if (e.RightButton == MouseButtonState.Pressed || e.ChangedButton == MouseButton.Right)
            {
                drawingCancellationToken.Cancel();
            }

            if (e.ChangedButton != MouseButton.Left)
                return;

            Debug.WriteLine("UP");

            e.Handled = true;

            this.prevMouseLocation = (e.GetPosition(this.mapView));

            var mercatorPoint = ScreenToMap(this.prevMouseLocation).AsPoint();

            if (itWasPanningWhileDrawing)
            {
                //this.mapView.ReleaseMouseCapture();

                this.ResetMapViewEvents();

                this.Refresh();

                this.drawingLayer.AddSemiVertex(mercatorPoint);

                //this.mapView.CaptureMouse();

                this.mapView.MouseMove -= MapView_MouseMoveDrawing;
                this.mapView.MouseMove += MapView_MouseMoveDrawing;

                this.mapView.MouseDown -= MapView_MouseDownForPanWhileDrawing;
                this.mapView.MouseDown += MapView_MouseDownForPanWhileDrawing;

                this.mapView.MouseUp -= MapView_MouseUpForDrawing;
                this.mapView.MouseUp += MapView_MouseUpForDrawing;

                return;
            }

            //if (isFinished)
            //{
            //    Debug.WriteLine("UP Finished");


            //}
            //else
            //{
            this.drawingLayer.AddVertex(mercatorPoint);

            this.drawingLayer.AddSemiVertex(mercatorPoint);
            //}


        }

        private Task<sb.Primitives.Geometry> GetDrawing(DrawMode mode, bool display = false)
        {
            taskCompletionSource = new TaskCompletionSource<sb.Primitives.Geometry>();

            drawingCancellationToken = new CancellationTokenSource();

            this.displayDrawingPath = display;

            this.drawMode = mode;

            if (this.viewTransform == null || drawMode == DrawMode.Rectange || drawMode == DrawMode.Freehand)
                taskCompletionSource.SetCanceled();

            ResetMapViewEvents();

            this.SetCursor(Cursors.Cross);

            drawingCancellationToken.Token.Register(() =>
            {
                taskCompletionSource.TrySetCanceled();

                this.SetCursor(Cursors.Arrow);

                this.mapView.MouseUp -= MapView_MouseUpForDrawing;
                this.mapView.MouseDown -= MapView_MouseDownForStartDrawing;
                this.mapView.MouseMove -= MapView_MouseMoveDrawing;
                this.mapView.MouseDown -= MapView_MouseDownForPanWhileDrawing;

                this.RemoveLayer(drawingLayer);

                if (drawingLayer != null)
                {
                    RemoveEditableFeatureLayer(drawingLayer.GetLayer());
                }

                taskCompletionSource = null;

            }, useSynchronizationContext: false);

            this.mapView.MouseDown -= MapView_MouseDownForStartDrawing;
            this.mapView.MouseDown += MapView_MouseDownForStartDrawing;

            return taskCompletionSource.Task;

        }

        public async Task<sb.Primitives.Geometry> GetDrawingAsync(DrawMode mode, bool display = false)
        {
            try
            {
                if (drawingCancellationToken != null)
                {
                    drawingCancellationToken.Cancel();
                }

                this.Status = MapStatus.Drawing;

                var result = await GetDrawing(mode, display);

                this.Status = MapStatus.Idle;

                return result;
            }
            catch (TaskCanceledException)
            {
                this.Status = MapStatus.Idle;

                return null;
            }
            catch (Exception ex)
            {
                this.Status = MapStatus.Idle;

                throw ex;
            }
            finally
            {
                taskCompletionSource = null;

                drawingCancellationToken = null;

                this.Status = MapStatus.Idle;

                this.Pan();
            }
        }

        public void CancelGetDrawing()
        {
            if (this.drawingCancellationToken != null)
            {
                this.drawingCancellationToken.Cancel();
            }
        }

        #endregion

        #region Identify

        public List<System.Data.DataTable> GetFeatures(sb.Point point)
        {
            return GetFeatures(point.AsSqlGeometry());
        }

        public List<System.Data.DataTable> GetFeatures(SqlGeometry geometry)
        {
            List<System.Data.DataTable> result = new List<System.Data.DataTable>();

            foreach (var layer in this.Layers.OfType<VectorLayer>())
            {
                if (!layer.Type.HasFlag(LayerType.VectorLayer))
                    continue;

                var features = layer.DataSource.GetEntireFeaturesWhereIntersects(geometry);

                if (features != null)
                {
                    features.TableName = layer.LayerName;

                    result.Add(features);
                }
            }

            return result;
        }

        //this.map.GetFeatures(arg.AsSqlGeometry()

        #endregion

        #region Edit

        CancellationTokenSource editingCancellationToken;

        EditableFeatureLayer editingLayer;

        public void EditPolygon(List<sb.Point> wgs84Points)
        {
            if (wgs84Points == null || wgs84Points.Count < 1)
            {
                return;
            }

            var layer = new EditableFeatureLayer(
                            "edit",
                            wgs84Points.Select(i => Ham.CoordinateSystem.MapProjection.MapProjects.GeodeticToMercator(i)).Take(wgs84Points.Count - 1).ToList(),
                            this.viewTransform,
                            sb.Primitives.GeometryType.Polygon);

            layer.RequestRightClickOptions = (i1, i2, i3) =>
            {
                this.AddRightClickOptions(i1, i2, i3);
            };

            layer.RequestRemoveRightClickOptions = () => { this.RemoveRightClickOptions(); };

            layer.Refresh = l =>
            {
                //this.ClearLayer(LayerType.EditableItem, false);

                this.RemoveLayer(l);

                this.SetLayer(l);

                AddEditableFeatureLayer(l);
            };

            this.SetLayer(layer);

        }

        public void Edit(sb.Primitives.Geometry geometry)
        {
            var layer = new EditableFeatureLayer(
                            "edit", geometry,
                            this.viewTransform);

            layer.RequestRightClickOptions = (i1, i2, i3) =>
            {
                this.AddRightClickOptions(i1, i2, i3);
            };

            layer.RequestRemoveRightClickOptions = () => { this.RemoveRightClickOptions(); };

            layer.Refresh = l =>
            {
                //this.ClearLayer(LayerType.EditableItem, false);

                this.RemoveLayer(l);

                this.SetLayer(l);

                AddEditableFeatureLayer(l);
            };

            this.SetLayer(layer);
        }

        private Task<sb.Primitives.Geometry> EditGeometry(sb.Primitives.Geometry geometry)
        {
            editingCancellationToken = new CancellationTokenSource();

            var tcs = new TaskCompletionSource<sb.Primitives.Geometry>();

            if (editingLayer != null)
            {
                this.RemoveEditableFeatureLayer(editingLayer);
            }

            editingLayer = new EditableFeatureLayer(
                            "edit", geometry,
                            this.viewTransform,
                            isNewDrawing: false);

            editingLayer.RequestRightClickOptions = (i1, i2, i3) =>
            {
                this.AddRightClickOptions(i1, i2, i3);
            };

            editingLayer.RequestRemoveRightClickOptions = () => { this.RemoveRightClickOptions(); };

            editingLayer.Refresh = l =>
            {
                this.RemoveEditableFeatureLayer(l);

                this.SetLayer(l);

                AddEditableFeatureLayer(l);
            };

            editingLayer.RequestFinishEditing = (g) =>
            {
                tcs.SetResult(g);

                this.RemoveEditableFeatureLayer(editingLayer);

                editingLayer = null;
            };

            this.SetLayer(editingLayer);

            AddEditableFeatureLayer(editingLayer);

            editingCancellationToken.Token.Register(() =>
            {
                tcs.TrySetCanceled();

                this.SetCursor(Cursors.Arrow);

                this.RemoveEditableFeatureLayer(editingLayer);

                tcs = null;

            }, useSynchronizationContext: false);

            return tcs.Task;
        }

        public async Task<sb.Primitives.Geometry> EditGeometryAsync(sb.Primitives.Geometry originalGeometry)
        {
            try
            {
                if (editingCancellationToken != null)
                {
                    editingCancellationToken.Cancel();
                }

                this.Status = MapStatus.EditingGeometry;

                var result = await EditGeometry(originalGeometry);

                this.Status = MapStatus.Idle;

                return result;
            }
            catch (TaskCanceledException)
            {
                this.Status = MapStatus.Idle;

                return originalGeometry;
            }
            catch (Exception ex)
            {
                this.Status = MapStatus.Idle;

                throw ex;
            }
        }

        public void CancelEditGeometry()
        {
            if (editingCancellationToken != null)
            {
                editingCancellationToken.Cancel();
            }
        }

        public void FinishEditing()
        {
            editingLayer?.FinishEditing();
        }

        #endregion


        #region Panable Path

        private void AddPanablePathToMapView(Path path)
        {
            if (!this.mapView.Children.Contains(path))
            {
                path.RenderTransform = this.panTransformForPoints;

                this.mapView.Children.Add(path);
            }
        }

        private void RemovePathFromMapView(Path path)
        {
            if (this.mapView.Children.Contains(path))
            {
                this.mapView.Children.Remove(path);
            }
        }

        #endregion

        #region Bezier

        CancellationTokenSource bezierCancellationToken;

        //public async Task DrawBezier()
        //{
        //    var start = (sb.Point)(await GetDrawingAsync(DrawMode.Point)).Geometry.Points.First();

        //    var end = (sb.Point)(await GetDrawingAsync(DrawMode.Point)).Geometry.Points.First();

        //    var startBezier = new Point(start.X, start.Y);

        //    var endBezier = new Point(end.X, end.Y);

        //    var bezierSegment = new BezierSegment(
        //                            viewTransform.Transform(startBezier),
        //                            viewTransform.Transform(endBezier),
        //                            viewTransform.Transform(end.AsWpfPoint()),
        //                            true);

        //    PathFigure figure = new PathFigure() { StartPoint = viewTransform.Transform(start.AsWpfPoint()) };
        //    figure.Segments.Add(bezierSegment);

        //    PathGeometry pathGeometry = new PathGeometry(new List<PathFigure>() { figure });
        //    pathGeometry.Transform = this.panTransformForPoints;

        //    PathFigure fig2 = new PathFigure() { StartPoint = viewTransform.Transform(start.AsWpfPoint()) };
        //    fig2.Segments.Add(new LineSegment(viewTransform.Transform(startBezier), true));

        //    PathFigure fig3 = new PathFigure() { StartPoint = viewTransform.Transform(end.AsWpfPoint()) };
        //    fig3.Segments.Add(new LineSegment(viewTransform.Transform(endBezier), true));

        //    Path temp = new Path();
        //    temp.Data = pathGeometry;
        //    temp.Stroke = new SolidColorBrush(Colors.Black);
        //    temp.StrokeThickness = 2;
        //    //temp.RenderTransform = this.panTransformForPoints;
        //    temp.Tag = new LayerTag(0) { IsTiled = false, LayerType = LayerType.Drawing };

        //    this.mapView.Children.Add(temp);

        //    Path axLine2 = new Path() { Stroke = new SolidColorBrush(Colors.Gray), StrokeThickness = 1, RenderTransform = panTransformForPoints };
        //    axLine2.Data = new PathGeometry(new List<PathFigure>() { fig2 });
        //    axLine2.Tag = new LayerTag(-1) { IsTiled = false, LayerType = LayerType.Drawing };
        //    this.mapView.Children.Add(axLine2);

        //    Path axLine3 = new Path() { Stroke = new SolidColorBrush(Colors.Gray), StrokeThickness = 1, RenderTransform = panTransformForPoints };
        //    axLine3.Data = new PathGeometry(new List<PathFigure>() { fig3 });
        //    axLine3.Tag = new LayerTag(-1) { IsTiled = false, LayerType = LayerType.Drawing };
        //    this.mapView.Children.Add(axLine3);


        //    //Geometry g = Geometry.Parse("F1 M 22.6563,23.75L 24.6563,25.75L 31.9063,32.75L 25.4063,32.75L 15.9063,23.75L 25.4063,14.75L 31.9063,14.75L 24.6563,21.75L 22.6563,23.75 Z");

        //    //Centered
        //    Geometry g = Geometry.Parse("F1 M 6.75,9L 8.75,11L 16,18L 9.5,18L 0,9L 9.5,0L 16,0L 8.75,7L 6.75,9 Z");
        //    //Geometry g = Geometry.Parse("F1 M 0,-1.52588e-005L 16,15.9986L 0,31.9986L 8.94823,15.9986L 0,-1.52588e-005 Z ");


        //    var bound = g.Bounds;

        //    Action update = () =>
        //    {
        //        ClearLayer(LayerType.AnimatingItem, false);

        //        for (int i = 0; i <= 10; i++)
        //        {
        //            double fraction = (i) / 10.0;

        //            Point location, direction;

        //            pathGeometry.GetPointAtFractionLength(fraction, out location, out direction);

        //            Path tempPath = new Path() { Fill = new SolidColorBrush(Colors.Red), Data = g };

        //            Matrix matrix = Matrix.Identity;

        //            var rotation = Math.Atan2(direction.Y, direction.X) * 180 / Math.PI;

        //            matrix.RotateAt(rotation, (bound.Width + bound.X) / 2.0, (bound.Height + bound.Y) / 2.0);

        //            matrix.Translate(location.X - bound.Width / 2.0 - bound.X / 2.0, location.Y - bound.Height / 2.0 - bound.Y / 2.0);

        //            tempPath.RenderTransform = new MatrixTransform(matrix);

        //            tempPath.Tag = new LayerTag(-1) { IsTiled = false, LayerType = LayerType.AnimatingItem };

        //            mapView.Children.Add(tempPath);

        //            if (!mapView.Children.Contains(temp))
        //            {
        //                mapView.Children.Add(temp);
        //            }
        //        }
        //    };

        //    var startLocateable = new Locateable(Projection.MercatorToGeodetic(start)) { Element = new Common.View.MapMarkers.Circle(1, new SolidColorBrush(Colors.Green)) };
        //    startLocateable.OnPositionChanged += (s, e) =>
        //    {
        //        var l = (Locateable)s;
        //        figure.StartPoint = viewTransform.Transform(new Point(l.X, l.Y));
        //        fig2.StartPoint = viewTransform.Transform(new Point(l.X, l.Y));
        //        update();

        //    };


        //    var endLocateable = new Locateable(Projection.MercatorToGeodetic(end)) { Element = new Common.View.MapMarkers.Circle(1, new SolidColorBrush(Colors.Green)) };
        //    endLocateable.OnPositionChanged += (s, e) =>
        //    {
        //        var l = (Locateable)s;
        //        bezierSegment.Point3 = viewTransform.Transform(new Point(l.X, l.Y));
        //        fig3.StartPoint = viewTransform.Transform(new Point(l.X, l.Y));

        //        update();
        //    };


        //    var startBezierLocateable = new Locateable(Projection.MercatorToGeodetic(startBezier.AsPoint())) { Element = new Common.View.MapMarkers.Circle(1, new SolidColorBrush(Colors.Green)) };
        //    startBezierLocateable.OnPositionChanged += (s, e) =>
        //    {
        //        var l = (Locateable)s;
        //        bezierSegment.Point1 = viewTransform.Transform(new Point(l.X, l.Y));
        //        (fig2.Segments.First() as LineSegment).Point = viewTransform.Transform(new Point(l.X, l.Y));
        //        update();
        //    };


        //    var endBezierLocateable = new Locateable(Projection.MercatorToGeodetic(endBezier.AsPoint())) { Element = new Common.View.MapMarkers.Circle(1, new SolidColorBrush(Colors.Green)) };
        //    endBezierLocateable.OnPositionChanged += (s, e) =>
        //    {
        //        var l = (Locateable)s;
        //        bezierSegment.Point2 = viewTransform.Transform(new Point(l.X, l.Y));
        //        (fig3.Segments.First() as LineSegment).Point = viewTransform.Transform(new Point(l.X, l.Y));
        //        update();
        //    };


        //    var specialLayer = new SpecialPointLayer("temp", new List<Locateable> { startLocateable, startBezierLocateable, endBezierLocateable, endLocateable }, 1, ScaleInterval.All, LayerType.MoveableItem);

        //    this.AddSpecialPointLayerToMap(specialLayer);
        //    update();
        //}

        //public async Task DrawBezier2()
        //{
        //    var polyline = (await GetDrawingAsync(DrawMode.Polyline)).Geometry.Points.Cast<sb.Point>().ToList();

        //    PolyBezierLayer layer = new PolyBezierLayer(polyline, this.viewTransform);

        //    layer.RequestRightClickOptions = (i1, i2, i3) =>
        //    {
        //        this.AddRightClickOptions(i1, i2, i3);
        //    };

        //    layer.RequestRemoveRightClickOptions = () => { this.RemoveRightClickOptions(); };

        //    layer.RequestRefresh = l =>
        //      {
        //          this.RemoveLayer(l);

        //          this.SetLayer(l);

        //          AddPolyBezierLayer(l);
        //      };

        //    this.SetLayer(layer);

        //    this.AddPolyBezierLayer(layer);

        //}

        private async Task<PolyBezierLayer> GetBezier(Geometry decoration, VisualParameters decorationVisual)
        {
            bezierCancellationToken = new CancellationTokenSource();

            var tcs = new TaskCompletionSource<PolyBezierLayer>();

            bezierCancellationToken.Token.Register(() =>
            {
                tcs.TrySetCanceled();

                this.SetCursor(Cursors.Arrow);

            }, useSynchronizationContext: false);

            var polylineGeometry = await GetDrawingAsync(DrawMode.Polyline);

            if (polylineGeometry == null)
            {
                bezierCancellationToken.Cancel();
            }
            else
            {
                var polyline = polylineGeometry.Points.Cast<sb.Point>().ToList();

                PolyBezierLayer layer = new PolyBezierLayer(polyline, this.viewTransform, decoration, decorationVisual);

                RegisterPolyBezierLayer(layer);

                layer.RequestFinishEditing = r =>
                {
                    tcs.SetResult(r);
                };

                this.SetLayer(layer);

                this.AddPolyBezierLayer(layer);
            }

            return await tcs.Task;
        }

        private void RegisterPolyBezierLayer(PolyBezierLayer layer)
        {
            layer.RequestRightClickOptions = (i1, i2, i3) =>
            {
                this.AddRightClickOptions(i1, i2, i3);
            };

            layer.RequestRemoveRightClickOptions = () => { this.RemoveRightClickOptions(); };

            layer.RequestRefresh = l =>
            {
                this.RemoveLayer(l);

                this.SetLayer(l);

                AddPolyBezierLayer(l);
            };

            layer.RequestAddLayer = l =>
            {
                if (l is SpecialLineLayer)
                {
                    AddSpecialLineLayer(l as SpecialLineLayer, () =>
                    {
                        layer.IsBezierShown = !layer.IsBezierShown;

                        if (layer.IsBezierShown)
                        {
                            AddComplexLayer(layer.GetMainPointLayer());

                            AddComplexLayer(layer.GetControlPointLayer());

                            AddPanablePathToMapView(layer.GetMainPath());

                            AddPanablePathToMapView(layer.GetControlPath());
                        }
                        else
                        {
                            RemoveLayer(layer.GetMainPointLayer());

                            RemoveLayer(layer.GetControlPointLayer());

                            RemovePathFromMapView(layer.GetMainPath());

                            RemovePathFromMapView(layer.GetControlPath());
                        }
                    });
                }
                else if (l is SpecialPointLayer)
                {
                    AddComplexLayer(l as SpecialPointLayer);
                }
            };

            layer.RequestRemoveLayer = l => this.RemoveLayer(l);

        }

        public async Task<PolyBezierLayer> GetBezierAsync(Geometry decoration = null, VisualParameters decorationVisual = null)
        {
            try
            {
                if (bezierCancellationToken != null)
                {
                    bezierCancellationToken.Cancel();
                }

                this.Status = MapStatus.Drawing;

                var result = await GetBezier(decoration, decorationVisual);

                this.Status = MapStatus.Idle;

                if (result != null)
                {
                    RemovePolyBezierLayer(result);
                }

                return result;
            }
            catch (TaskCanceledException)
            {
                this.Status = MapStatus.Idle;

                return null;
            }
            catch (Exception ex)
            {
                this.Status = MapStatus.Idle;

                throw ex;
            }
        }

        #endregion
    }
}