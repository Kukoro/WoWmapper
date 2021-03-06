﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WoWmapper.Controllers;
using DS4Windows;
using WoWmapper.Controllers.DS4;
using WoWmapper.Controllers.Xbox;
using System.Drawing;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WoWmapper.Properties;
using J2i.Net.XInputWrapper;
using WoWmapper.Classes;
using WoWmapper.Input;

namespace WoWmapper.Controllers
{
    public static class ControllerManager
    {
        public static event UpdateEventHandler ControllerChanged;
        public static event UpdateEventHandler ControllersUpdated;
        public delegate void UpdateEventHandler();

        private static Thread _watcherThread;
        private static IController _activeController;
        private static List<IController> _allControllers = new List<IController>(); 
        private const int ThreadRefreshRate = 1000;

        public static void Start()
        {
            if (_watcherThread != null) return;
            Logger.Write("Starting controller manager");
            _watcherThread = new Thread(ControllerWatcher) {IsBackground=true};
            _watcherThread.Start();
            if(Settings.Default.EnableXbox)
                XboxController.StartPolling();
            if(Settings.Default.EnableDS4)
                DS4Devices.findControllers();
        }

        public static void Stop()
        {
            if (_watcherThread == null) return;
            Logger.Write("Stopping controller manager");
            _watcherThread.Abort();
            _watcherThread = null;
            foreach (var controller in _allControllers) controller?.Stop();
            _allControllers.Clear();
            XboxController.StopPolling();
            DS4Devices.stopControllers();
        }

        public static List<IController> CurrentControllers => _allControllers?.ToList();

        private static void ControllerWatcher()
        {
            while (true)
            {
                if (_activeController != null && !_activeController.IsAlive())
                {
                    Logger.Write($"Controller disconnected");
                    RemoveDevice(_activeController);
                    _activeController = null;
                }

                if (_activeController != null)
                {
                    if ((_activeController.Type == ControllerType.DualShock4 && !Settings.Default.EnableDS4) ||
                        _activeController.Type == ControllerType.Xbox && !Settings.Default.EnableXbox)
                    {
                        RemoveDevice(_activeController);
                        _activeController = null;
                    }
                }

                ScanDevices();

                if (_activeController == null)
                {

                    if (Settings.Default.AutoConnectDevice)
                    {
                        // Connect first device
                        ActivateFirst();
                    }

                }

                Thread.Sleep(ThreadRefreshRate);
            }
        }

        private static void RemoveDevice(IController device)
        {
            device.Stop();
            _allControllers.RemoveAll(dev => dev.Equals(device));
            device = null;
            ControllersUpdated?.Invoke();
        }

        public static void ScanDevices()
        {
            // Scan for devices
            lock (_allControllers)
            {
                if (Settings.Default.EnableDS4)
                {
                    try
                    {
                        DS4Devices.findControllers();

                        var listDS4 = DS4Devices.getDS4Controllers();

                        foreach (var controller in listDS4)
                        {
                            if (_allControllers.Count(c => c.UnderlyingDevice == controller.HidDevice) == 0)
                            {
                                Logger.Write("Found new DS4 device: {0}", controller.MacAddress);
                                _allControllers.Add(new DS4Input(controller.MacAddress));
                                ControllersUpdated?.Invoke();
                            }

                        }
                    }
                    catch (ThreadAbortException ex)
                    { }
                    catch (Exception ex)
                    {
                        Logger.Write("DS4 driver error: ", ex);
                        MessageBox.Show(string.Format(Resources.ErrorDriverDs4Disabled, ex.Message), Resources.ErrorDriverDisabledTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                        Settings.Default.EnableDS4 = false;
                        Settings.Default.Save();
                    }
                }

                if (Settings.Default.EnableXbox)
                {
                    // Scan for Xbox controllers
                    try
                    {
                        for (int i = XboxController.FIRST_CONTROLLER_INDEX; i < XboxController.MAX_CONTROLLER_COUNT; i++)
                        {
                            XInputState stateController = new XInputState();
                            int result = XInput.XInputGetState(i, ref stateController);
                            if (result == 0) // Controller found
                            {
                                var controller = XboxController.RetrieveController(i);
                                if (_allControllers.Count(c => c.UnderlyingDevice == controller) == 0)
                                {
                                    Logger.Write("Found new XInput device: {0}", i);
                                    _allControllers.Add(new XboxInput(i));
                                    ControllersUpdated?.Invoke();
                                }

                            }
                        }
                    }
                    catch (ThreadAbortException ex)
                    { }
                    catch (Exception ex)
                    {
                        Logger.Write("XInput driver error: ", ex);
                        MessageBox.Show(string.Format(Resources.ErrorDriverXboxDisabled, ex.Message), Resources.ErrorDriverDisabledTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                        Settings.Default.EnableXbox = false;
                        Settings.Default.Save();
                    }
                }
            }
        }

        public static void SetActiveController(IController Controller)
        {
            _activeController?.SetLightbar(0,0,0);
            _activeController = Controller;

            ControllerChanged?.Invoke();
            ControllersUpdated?.Invoke();
            MainWindow.UpdateChildren();
        }

        public static void ActivateFirst()
        {
            if (_allControllers == null) return;

            if (_allControllers.Count <= 0) return;
            try
            {
                SetActiveController(_allControllers.First());
            } catch (Exception ex) { }
        }

        public static IController GetActiveController()
        {
            return _activeController;
        }
    }


}
