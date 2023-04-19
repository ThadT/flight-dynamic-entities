using Esri.ArcGISRuntime;
using Esri.ArcGISRuntime.ArcGISServices;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Mapping.Labeling;
using Esri.ArcGISRuntime.RealTime;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.Tasks.Geocoding;
using Esri.ArcGISRuntime.UI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Flights
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        #region Constants
        // - main map
        const BasemapStyle MainMapBasemap = BasemapStyle.ArcGISChartedTerritory;
        // - tracking scene
        const BasemapStyle TrackingSceneBasemap = BasemapStyle.ArcGISImagery;
        const string ElevationServiceUrl = "http://elevation3d.arcgis.com/arcgis/rest/services/WorldElevation3D/Terrain3D/ImageServer";
        const double SceneElevationExaggeration = 2.5;
        // - weather map (precipitation radar)
        const string WeatherMapItemUrl = "https://www.arcgis.com/home/item.html?id=629b61c7505547d5b4abad400ca2d049";
        //const string PrecipRadarLayerUrl = "http://mesonet.agron.iastate.edu/cgi-bin/wms/nexrad/n0q.cgi";
        // Geocode service (for finding airports)
        const string GeocodeServiceUrl = "https://geocode-api.arcgis.com/arcgis/rest/services/World/GeocodeServer";
        
        // TODO: Provide a URL to a FlightAware stream service.
        //       (You can use another stream service, but may need to update field names and other details)
        const string FlightAwareStreamUrl = "";
        #endregion

        public MainWindow()
        {
            InitializeComponent();

            // Call functions to create the maps/scenes and graphics overlays.
            SetupGeoModels();
            _ = CreateGraphicsOverlays();

            // Set the code behind as the data context
            // (some properties will be bound to UI elements).
            this.DataContext = this;
        }

        #region Start up functions
        private async void SetupGeoModels()
        {
            // GeoModel == Map or Scene
            // Main map to display dynamic entities
            var mainMap = new Map(MainMapBasemap);

            // Weather map (to show weather radar at the destination or origin airport).
            var weatherMap = new Map(new Uri(WeatherMapItemUrl));

            // Scene to show one entity (follow a graphic, eg) ...
            // Create an elevation source to show relief in the scene.
            var elevationSource = new ArcGISTiledElevationSource(new Uri(ElevationServiceUrl));

            // Create a Surface with the elevation data.
            var elevationSurface = new Surface
            {
                ElevationExaggeration = SceneElevationExaggeration
            };
            elevationSurface.ElevationSources.Add(elevationSource);

            var trackingScene = new Scene(TrackingSceneBasemap)
            {
                BaseSurface = elevationSurface
            };

            // Set the initial extent to the southwestern United States.
            var initialExtent = new Envelope(-120.5, 32.0, -110.0, 38.0, SpatialReferences.Wgs84);
            mainMap.InitialViewpoint = new Viewpoint(initialExtent);
            weatherMap.InitialViewpoint = new Viewpoint(initialExtent);
            trackingScene.InitialViewpoint = new Viewpoint(initialExtent);

            MainMapView.Map = mainMap;
            WeatherMapView.Map = weatherMap;
            TrackingSceneView.Scene = trackingScene;
        }

        private async Task CreateGraphicsOverlays()
        {
            // Create a GraphicsOverlay to show the selected airport on the weather map.
            var graphicsOverlayAirport = new GraphicsOverlay();
            var airportSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Yellow, 20);
            var rendererAirports = new SimpleRenderer(airportSymbol);
            var airportLabelSymbol = new TextSymbol
            {
                FontFamily = "Arial",
                Color = System.Drawing.Color.Navy,
                HaloColor = System.Drawing.Color.White,
                HaloWidth = 2,
                Size = 20
            };
            var airportLabelExp = new ArcadeLabelExpression("return $feature.airportcode;");
            var airportLabelDef = new LabelDefinition(airportLabelExp, airportLabelSymbol)
            {
                WhereClause = "1=1",
                Placement = LabelingPlacement.PointBelowCenter
            };
            graphicsOverlayAirport.LabelDefinitions.Add(airportLabelDef);
            graphicsOverlayAirport.Renderer = rendererAirports;
            graphicsOverlayAirport.LabelsEnabled = true;

            WeatherMapView.GraphicsOverlays?.Add(graphicsOverlayAirport);

            // Create a GraphicsOverlay to display a graphic for the selected dynamic entity in the scene view.
            var graphicsOverlayTrackEntity = new GraphicsOverlay();
            var modelPath = @"C:\Code\ArcGISMapsSDK_ConsumeStreamService\3D\bristol.dae";
            var plane3DSymbol = await ModelSceneSymbol.CreateAsync(new Uri(modelPath), 1.0);
            var rendererTrackEntity = new SimpleRenderer(plane3DSymbol);

            // Set a heading expression to orient the plane symbol with its heading.
            rendererTrackEntity.SceneProperties.HeadingExpression = "[heading]";

            graphicsOverlayTrackEntity.Renderer = rendererTrackEntity;
            graphicsOverlayTrackEntity.SceneProperties.SurfacePlacement = SurfacePlacement.RelativeToScene;

            TrackingSceneView.GraphicsOverlays?.Add(graphicsOverlayTrackEntity);
        }
        #endregion

        #region Properties

        // ArcGIS Stream Service data source.
        private ArcGISStreamService _streamService;
        // Dynamic entity layer to display flights.
        private DynamicEntityLayer _dynamicEntityLayer;
        // The selected destination airport.
        private string _selectedAirport;
        // A graphic to show the selected flight in a scene view.
        private Graphic _planeGraphic;

        // Collection of flights going to the selected airport.
        private ConcurrentDictionary<string, long> _dynamicEntityIdDictionary = new();
        public ObservableCollection<DynamicEntity> DynamicEntityCollection { get; } = new ObservableCollection<DynamicEntity>();

        // Collection of unique destinations (airport codes) for flights in the layer.
        private ConcurrentDictionary<string, string> _uniqueAirportCodesDictionary = new();
        public ObservableCollection<string> CurrentAirports { get; } = new ObservableCollection<string>();

        // The selected flight.
        private DynamicEntity _selectedFlight;
        public DynamicEntity SelectedFlight
        {
            get { return _selectedFlight; }
            set
            {
                _selectedFlight = value;
                RaisePropertyChanged(nameof(SelectedFlight));
            }
        }

        // Count of dynamic entities currently displayed.
        private int _dynamicEntityCount = 0;
        public int DynamicEntityCount
        {
            get { return _dynamicEntityCount; }
            set
            {
                _dynamicEntityCount = value;
                RaisePropertyChanged(nameof(DynamicEntityCount));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void RaisePropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
        #endregion

        #region Utilities
        private async void StartStreamService()
        {
            // Create the stream service from the URL (passed into the view model constructor).
            _streamService = new ArcGISStreamService(new Uri(FlightAwareStreamUrl));

            _streamService.ConnectionStatusChanged += (e, status) => 
            { 
                Dispatcher.Invoke(()=>ConnectionStatusTextBlock.Text = $"{status}");
            };
            // Set option to purge updates older than 12 hours.
            _streamService.PurgeOptions.MaximumDuration = new TimeSpan(12, 0, 0);
            // Keep no more than 50 observations per track.
            _streamService.PurgeOptions.MaximumObservationsPerTrack = 50;
            
            // Filter the service to show flights above 10,000 feet.
            var streamFilter = new ArcGISStreamServiceFilter
            {
                WhereClause = "alt >= 10000"
            };
            // If requested, also filter by the current map extent.
            if (FilterWithExtentCheckBox.IsChecked == true)
            {
                var extent = MainMapView.GetCurrentViewpoint(ViewpointType.BoundingGeometry)?.TargetGeometry;
                streamFilter.Geometry = extent;
            }
            _streamService.Filter = streamFilter;

            // Setup the notification handlers for new dynamic entities and observations.
            _streamService.DynamicEntityReceived += StreamService_DynamicEntityReceived;
            _streamService.DynamicEntityObservationReceived += StreamService_DynamicEntityObservationReceived;
            _streamService.DynamicEntityPurged += StreamService_DynamicEntityPurged;

            // Create a dynamic entity layer to show the stream data.
            _dynamicEntityLayer = new DynamicEntityLayer(_streamService)
            {
                // Create and apply a simple renderer with a red circle.
                Renderer = new SimpleRenderer(new SimpleMarkerSymbol
                (SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Red, 6))
            };

            var trackSym = new SimpleLineSymbol(SimpleLineSymbolStyle.DashDotDot, System.Drawing.Color.RebeccaPurple, 2);
            var trackLineRenderer = new SimpleRenderer(trackSym);
            var previousObsSym = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Diamond, System.Drawing.Color.DarkMagenta, 5);
            var previousObsRenderer = new SimpleRenderer(previousObsSym);

            _dynamicEntityLayer.TrackDisplayProperties.TrackLineRenderer = trackLineRenderer;
            _dynamicEntityLayer.TrackDisplayProperties.PreviousObservationRenderer = previousObsRenderer;
            // Call a function to define labels.
            SetupLabeling();

            // Load and connect the stream service.
            await _streamService.LoadAsync();
            await _streamService.ConnectAsync();

            // Add the layer to the map.
            MainMapView.Map.OperationalLayers.Add(_dynamicEntityLayer);

            // Handle the connection change event (clean up when closed).
            _streamService.ConnectionStatusChanged += StreamConnectionStatusChanged;
        }

        private UniqueValueRenderer CreateRendererForDestination(string airportCode)
        {
            var uvr = new UniqueValueRenderer();
            var defaultSym = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.RoyalBlue, 4);
            var destinationSym = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Red, 8);
            uvr.DefaultSymbol = defaultSym;
            var destinationValue = new UniqueValue(airportCode, airportCode, destinationSym, airportCode);
            uvr.UniqueValues.Add(destinationValue);
            uvr.FieldNames.Add("dest");

            return uvr;
        }

        private void SetupLabeling()
        {
            _dynamicEntityLayer.LabelDefinitions.Clear();
            var labelExp = new ArcadeLabelExpression("return $feature.ident");
            var labelSym = new TextSymbol
            {
                FontFamily = "Arial",
                Color = System.Drawing.Color.Navy,
                HaloColor = System.Drawing.Color.White,
                Size = 14
            };
            var labelDef = new LabelDefinition(labelExp, labelSym);

            _dynamicEntityLayer.LabelDefinitions.Add(labelDef);
        }

        private async Task<MapPoint> GeocodeAirportCode(string airportCode)
        {
            var locator = await LocatorTask.CreateAsync(new Uri(GeocodeServiceUrl));
            var geocodeResults = await locator.GeocodeAsync(airportCode);
            var topResult = geocodeResults.FirstOrDefault();
            if (topResult == null) { return null; }

            return topResult.DisplayLocation;
        }

        private void StartTrackingPlane(GeoElement plane)
        {
            // Clear existing graphics in the scene view.
            TrackingSceneView.GraphicsOverlays.First().Graphics.Clear();

            // Get the flight's altitude for the graphic point z-value.
            double planeAltitude = 0.0;
            double.TryParse(plane.Attributes["alt"]?.ToString(), out planeAltitude);

            var planeLocation = plane.Geometry as MapPoint;
            var pointZ = new MapPoint(planeLocation.X, planeLocation.Y, planeAltitude, planeLocation.SpatialReference);

            // Create a new graphic with flight ID and heading attributes.
            var attributes = new Dictionary<string, object>
            {
                {"ident", plane.Attributes["ident"]},
                {"heading", plane.Attributes["heading"] }
            };
            _planeGraphic = new Graphic(pointZ, attributes);

            // Add the graphic.
            TrackingSceneView.GraphicsOverlays.First().Graphics.Add(_planeGraphic);

            // Apply an orbit camera controller to follow the graphic.
            TrackingSceneView.CameraController = new OrbitGeoElementCameraController(_planeGraphic, 100.0);
        }
        #endregion

        #region Stream event handlers
        // Handle notifications for new dynamic entities.
        private void StreamService_DynamicEntityReceived(object sender, DynamicEntityEventArgs e)
        {
            DynamicEntityCount++;
        }

        private void StreamService_DynamicEntityPurged(object sender, DynamicEntityEventArgs e)
        {
            DynamicEntityCount--;

            // Get the dynamic entity being purged.
            var dynamicEntity = e.DynamicEntity;

            // Get the flight ID.
            var flightId = dynamicEntity.Attributes["ident"]?.ToString();

            if (!string.IsNullOrEmpty(flightId))
            {
                // Try to remove it from the ConcurrentDictionary.
                if (_dynamicEntityIdDictionary.TryRemove(flightId, out long entityId))
                {
                    // If removed, also remove from the collection.
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        DynamicEntityCollection.Remove(dynamicEntity);
                    });
                }
            }
        }

        // Handle notifications for new observations.
        private void StreamService_DynamicEntityObservationReceived(object sender, DynamicEntityObservationEventArgs e)
        {
            // Get the attributes dictionary for this observation.
            var attr = e.Observation.Attributes;

            // Make sure there's a value for the destination airport.
            if (attr["dest"] != null)
            {
                // Get the destination airport code as an upper case string.
                var thisDestination = attr["dest"].ToString().ToUpper();

                // Only look for North American airports (indicated by initial letter).
                string[] prefixesCanadaMexicoUSA = { "K", "C", "M" };
                if (!prefixesCanadaMexicoUSA.Any(prefix => thisDestination.StartsWith(prefix))) { return; }
                // ----
                // See if this destination is already in the airport code list.
                // Add it to the list if it isn't.
                // ----
                if (_uniqueAirportCodesDictionary.TryAdd(thisDestination, thisDestination))
                {
                    // If this is a new airport code, insert it into the observable collection.
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        CurrentAirports.Add(thisDestination);
                    });
                }
                // ----
                // If there's a selected destination, see if this flight is going there.
                // If it is, add the flight to the collection shown in the list box.
                // ----
                // See if this observation's destination ("dest") matches the selected airport.
                if (_selectedAirport is not null && thisDestination == _selectedAirport)
                {
                    // The "ident" field holds the flight ID ("DAL1234", eg).
                    var flightId = attr["ident"]?.ToString();

                    if (!string.IsNullOrEmpty(flightId))
                    {
                        // Get the dynamic entity to which this observation applies.
                        var dynamicEntity = e.Observation.GetDynamicEntity();
                        // Call 'TryAdd' on the ConcurrentDictionary (returns false if it already exists).
                        if (_dynamicEntityIdDictionary.TryAdd(flightId, dynamicEntity.EntityId))
                        {
                            // If this is a new flight ID, insert it into the observable collection.
                            Application.Current?.Dispatcher?.Invoke(() =>
                            {
                                DynamicEntityCollection.Add(dynamicEntity);
                            });
                        }
                    }
                }
            }
        }

        // Handle notifications for updates to the selected dynamic entity (flight).
        private void SelectedFlight_DynamicEntityChanged(object sender, DynamicEntityChangedEventArgs e)
        {
            // Get updates from a new observation.
            var obs = e.ReceivedObservation;
            if (obs != null)
            {
                // Get the updated altitude for the graphic point z-value.
                double planeAltitude = 0.0;
                double.TryParse(obs.Attributes["alt"]?.ToString(), out planeAltitude);

                var updatedLocation = obs.Geometry as MapPoint;
                var pointZ = new MapPoint(updatedLocation.X, updatedLocation.Y, planeAltitude, updatedLocation.SpatialReference);

                Dispatcher.Invoke(() =>
                {
                    // Update the geometry and the heading attribute.
                    _planeGraphic.Geometry = pointZ;
                    _planeGraphic.Attributes["heading"] = obs.Attributes["heading"];
                });
            }
        }
        private void StreamConnectionStatusChanged(object sender, ConnectionStatus e)
        {
            // When the stream is disconnected, detach event handlers and clean up.
            if (e == ConnectionStatus.Disconnected)
            {
                // Remove notification handlers.
                _streamService.DynamicEntityReceived -= StreamService_DynamicEntityReceived;
                _streamService.DynamicEntityObservationReceived -= StreamService_DynamicEntityObservationReceived;
                _streamService.DynamicEntityPurged -= StreamService_DynamicEntityPurged;

                // Remove the dynamic entity layer from the map.
                MainMapView.Map.OperationalLayers.Remove(_dynamicEntityLayer);

                // Clear any airports or dynamic entities tracked for the service.
                _dynamicEntityIdDictionary = new();
                _uniqueAirportCodesDictionary = new();
                DynamicEntityCollection.Clear();
                CurrentAirports.Clear();

                // Clear the selected airport, flight, and reset entity count.
                _selectedAirport = null;
                SelectedFlight = null;
                DynamicEntityCount = 0;

                // Clear graphics and reset the map/scene viewpoints.
                var mainMapViewpoint = MainMapView.GetCurrentViewpoint(ViewpointType.BoundingGeometry);
                TrackingSceneView.GraphicsOverlays.First().Graphics.Clear();
                TrackingSceneView.SetViewpoint(mainMapViewpoint);
                WeatherMapView.GraphicsOverlays.First().Graphics.Clear();
                WeatherMapView.SetViewpoint(mainMapViewpoint);
            }
        }
        #endregion

        #region UI event handlers
        private void ZoomToDynamicEntity(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Should be one or zero selected dynamic entities in the layer at any time.
            var selectedFlight = _dynamicEntityLayer.GetSelectedDynamicEntities().FirstOrDefault();
            if (selectedFlight != null)
            {
                var extentBuilder = new EnvelopeBuilder(selectedFlight.Geometry.Extent);
                var flightViewpoint = new Viewpoint(extentBuilder.Center, 500000);
                MainMapView.SetViewpoint(flightViewpoint);
            }
        }

        private async void MainMapView_Tapped(object sender, Esri.ArcGISRuntime.UI.Controls.GeoViewInputEventArgs e)
        {
            // First dismiss any existing callout (previous identify, eg).
            MainMapView.DismissCallout();

            if (_dynamicEntityLayer is null) { return; }

            // Identify the dynamic entity layer using the tap/click location on the map.
            var idResult = await MainMapView.IdentifyLayerAsync(_dynamicEntityLayer, e.Position, 4, false, 1);

            // Get the first DynamicEntityObservation from the results.
            var obs = idResult.GeoElements.FirstOrDefault() as DynamicEntityObservation;

            // Get the DynamicEntity for this observation.
            // (if the user clicked a previous observation,
            // this will "jump" the callout to the last one).
            var de = obs?.GetDynamicEntity();

            if (de != null)
            {
                // Get the flight ID, departure and arrival airports.
                var flight = de.Attributes["ident"]?.ToString();
                var from = de.Attributes["orig"]?.ToString();
                var to = de.Attributes["dest"]?.ToString();
                var fromTo = $"From '{from}' To '{to}'";

                // Show the flight info in a callout at the entity location.
                var calloutDef = new CalloutDefinition(flight, fromTo);
                MainMapView.ShowCalloutForGeoElement(de, e.Position, calloutDef);
            }
        }

        private void StreamServiceConnectButton_Click(object sender, RoutedEventArgs e)
        {
            // Connect to or disconnect from the stream service.
            if (_streamService == null ||
                _streamService.ConnectionStatus == ConnectionStatus.Disconnected)
            {
                // Make sure a service URL was provided.
                if(string.IsNullOrEmpty(FlightAwareStreamUrl))
                {
                    MessageBox.Show("Please provide a URL for the FlightAware stream service (line 40 of MainWindow.xaml.cs)",
                        "No stream service URL", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                StartStreamService();
                FilterWithExtentCheckBox.IsEnabled = false;
                StreamServiceConnectButton.Content = "Disconnect from stream service";
            }
            else
            {
                // Disconnect from the stream service data source.
                _streamService.DisconnectAsync();

                // Update the UI.
                FilterWithExtentCheckBox.IsEnabled = true;
                StreamServiceConnectButton.Content = "Connect to stream service";
            }
        }

        private void MainMapView_ViewpointChanged(object sender, System.EventArgs e)
        {
            // Get the current map scale.
            var currentScale = MainMapView.MapScale;
            // Show tracks at scales larger than 1/500000
            // (otherwise hide them).
            var showTracks = (currentScale <= 500000);

            var _dynamicEntityLayer = MainMapView.Map.OperationalLayers.OfType<DynamicEntityLayer>().FirstOrDefault();
            // Layer might not be loaded yet.
            if (_dynamicEntityLayer != null)
            {
                // Toggle tracks, observations, and labels.
                _dynamicEntityLayer.TrackDisplayProperties.ShowTrackLine = showTracks;
                _dynamicEntityLayer.TrackDisplayProperties.ShowPreviousObservations = showTracks;
                _dynamicEntityLayer.LabelsEnabled = showTracks;
            }
        }

        private async void SelectedDestinationChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // Clear any previously tracked dynamic entities.
            DynamicEntityCollection.Clear();
            _dynamicEntityIdDictionary.Clear();

            if (e.AddedItems.Count > 0)
            {
                _selectedAirport = e.AddedItems[0].ToString();

                // Create a renderer to show flights to this airport.
                _dynamicEntityLayer.Renderer = CreateRendererForDestination(_selectedAirport);

                WeatherMapView.GraphicsOverlays.FirstOrDefault()?.Graphics.Clear();

                // Geocode to find the airport location from its code.
                var airportLocation = await GeocodeAirportCode(_selectedAirport);
                if (airportLocation == null) { return; }

                // Create a new graphic for the airport.
                var airportGraphic = new Graphic(airportLocation);
                airportGraphic.Attributes.Add("airportcode", _selectedAirport);
                WeatherMapView.GraphicsOverlays.FirstOrDefault()?.Graphics.Add(airportGraphic);

                // Zoom to the airport on the weather map.
                var airportViewpoint = new Viewpoint(airportLocation, 2000000);
                WeatherMapView.SetViewpoint(airportViewpoint);
            }
        }

        private void SelectedFlightChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // Clear any currrently selection.
            _dynamicEntityLayer.ClearSelection();
            if (SelectedFlight != null)
            {
                SelectedFlight.DynamicEntityChanged -= SelectedFlight_DynamicEntityChanged;
            }

            SelectedFlight = null;

            if (e.AddedItems.Count > 0)
            {
                // Get the item selected in the ListBox.
                SelectedFlight = e.AddedItems[0] as DynamicEntity;
                if (SelectedFlight == null) { return; }

                // Select the new flight.
                _dynamicEntityLayer.SelectDynamicEntity(SelectedFlight);

                // Start tracking the flight in the scene view.
                StartTrackingPlane(SelectedFlight);

                // Handle updates to this flight.
                SelectedFlight.DynamicEntityChanged += SelectedFlight_DynamicEntityChanged;
            }
        }
        #endregion

    }
}
