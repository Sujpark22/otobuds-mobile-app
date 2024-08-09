using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;

namespace otobuds_mobile_app
{
    public partial class MainPage : ContentPage
    {
        private readonly IAdapter _bluetoothAdapter;
        private IDevice _bluetoothDevice;
        private IService _uartService;
        private ICharacteristic _uartReadCharacteristic;
        private ICharacteristic _uartWriteCharacteristic;
        private ObservableCollection<IDevice> _deviceList;
        private static int _batteryLevel;

        public MainPage()
        {
            InitializeComponent();
            _bluetoothAdapter = CrossBluetoothLE.Current.Adapter;
            _deviceList = new ObservableCollection<IDevice>();
            DevicesListView.ItemsSource = _deviceList;

            _bluetoothAdapter.DeviceDiscovered += OnDeviceDiscovered;
        }

        private async void OnStartClicked(object sender, EventArgs e)
        {
            _deviceList.Clear();

            OutputLabel.Text = "Scanning for devices...";
            await _bluetoothAdapter.StartScanningForDevicesAsync();
        }

        private void OnDeviceDiscovered(object sender, DeviceEventArgs e)
        {
            if (!_deviceList.Contains(e.Device))
            {
                _deviceList.Add(e.Device);
            }
        }

        private async void OnDeviceSelected(object sender, SelectedItemChangedEventArgs e)
        {
            var selectedDevice = e.SelectedItem as IDevice;
            if (selectedDevice == null)
                return;

            OutputLabel.Text = "Connecting to " + selectedDevice.Name;

            try
            {
                await _bluetoothAdapter.StopScanningForDevicesAsync();

                // Connect to the selected device
                await _bluetoothAdapter.ConnectToDeviceAsync(selectedDevice);

                // Discover services
                var services = await selectedDevice.GetServicesAsync();
                var uartService = services.FirstOrDefault(s => s.Id == new Guid("YOUR_UART_SERVICE_UUID")); 

                if (uartService != null)
                {
                    _uartService = uartService;

                    // Discover characteristics
                    var characteristics = await uartService.GetCharacteristicsAsync();
                    _uartReadCharacteristic = characteristics.FirstOrDefault(c => c.Id == new Guid("YOUR_UART_READ_CHARACTERISTIC_UUID")); 
                    _uartWriteCharacteristic = characteristics.FirstOrDefault(c => c.Id == new Guid("YOUR_UART_WRITE_CHARACTERISTIC_UUID")); 

                    if (_uartReadCharacteristic == null || _uartWriteCharacteristic == null)
                    {
                        OutputLabel.Text = "UART characteristics not found.";
                        return;
                    }
                }
                else
                {
                    OutputLabel.Text = "UART service not found.";
                }
            }
            catch (Exception ex)
            {
                OutputLabel.Text = "Connection failed: " + ex.Message;
            }
        }

        private async void OnSendCommandClicked(object sender, EventArgs e)
        {
            var selectedCommand = CommandPicker.SelectedIndex;
            bool status = false;

            switch (selectedCommand)
            {
                case 0:
                    status = await WriteToCharacteristicAsync(new byte[] { 0x43, 0x50, 0x00 });
                    OutputLabel.Text = status ? "Play command sent" : "Play command failed";
                    break;
                case 1:
                    var chirpConfig = await GetChirpConfig();
                    status = await WriteToCharacteristicAsync(chirpConfig);
                    OutputLabel.Text = status ? "Chirp config command sent" : "Chirp config command failed";
                    break;
                case 2:
                    var micConfig = await GetMicConfig();
                    status = await WriteToCharacteristicAsync(micConfig);
                    OutputLabel.Text = status ? "Mic config command sent" : "Mic config command failed";
                    break;
                case 3:
                    OutputLabel.Text = "Battery Level: " + _batteryLevel + "%";
                    break;
                case 4:
                    await ScanAndConnectBleAsync();
                    break;
                case 5:
                    BleDisconnect();
                    break;
                case 6:
                    await DisplayAlert("Exit", "Exiting the application.", "OK");
                    System.Diagnostics.Process.GetCurrentProcess().CloseMainWindow();
                    break;
                default:
                    OutputLabel.Text = "Invalid command";
                    break;
            }
        }

        private async Task<bool> WriteToCharacteristicAsync(byte[] data)
        {
            if (_uartWriteCharacteristic != null)
            {
                try
                {
                    await _uartWriteCharacteristic.WriteAsync(data);
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error writing to characteristic: {ex.Message}");
                    return false;
                }
            }
            return false;
        }

        private async Task ScanAndConnectBleAsync()
        {
            _deviceList.Clear();
            _bluetoothAdapter.ScanTimeout = 5000;
            await _bluetoothAdapter.StartScanningForDevicesAsync();
        }

        private async Task ConnectDeviceAsync()
        {
            if (_bluetoothDevice == null)
            {
                await DisplayAlert("Error", "No device selected.", "OK");
                return;
            }

            try
            {
                await _bluetoothAdapter.ConnectToDeviceAsync(_bluetoothDevice);
                _uartService = await _bluetoothDevice.GetServiceAsync(Guid.Parse("YOUR_UART_SERVICE_UUID")); // Replace with your UART service UUID
                _uartReadCharacteristic = await _uartService.GetCharacteristicAsync(Guid.Parse("YOUR_UART_READ_CHARACTERISTIC_UUID")); // Replace with your read characteristic UUID
                _uartWriteCharacteristic = await _uartService.GetCharacteristicAsync(Guid.Parse("YOUR_UART_WRITE_CHARACTERISTIC_UUID")); // Replace with your write characteristic UUID

                if (_uartReadCharacteristic.CanRead)
                {
                    _uartReadCharacteristic.ValueUpdated += (o, args) =>
                    {
                        var bytes = args.Characteristic.Value;
                        ProcessReceivedData(bytes);
                    };
                    await _uartReadCharacteristic.StartUpdatesAsync();
                }

                await DisplayAlert("Success", "Connected to the device.", "OK");
            }
            catch (COMException comEx)
            {
                await DisplayAlert("COM Error", $"COM Exception: {comEx.Message}", "OK");
                System.Diagnostics.Debug.WriteLine($"COM Error: {comEx}");
            }
            catch (Exception ex)
            {
                await DisplayAlert("Connection Error", $"Failed to connect to the device: {ex.Message}", "OK");
                System.Diagnostics.Debug.WriteLine($"Error: {ex}");
            }
        }


        private void ProcessReceivedData(byte[] data)
        {
            if (data.Length == 3 && data[0] == 0x42 && data[1] == 0x4C)
            {
                _batteryLevel = data[2];
            }
        }

        private void BleDisconnect()
        {
            if (_bluetoothDevice != null)
            {
                _bluetoothAdapter.DisconnectDeviceAsync(_bluetoothDevice);
                _bluetoothDevice = null;
            }
        }

        private async Task<byte[]> GetChirpConfig()
        {
            short startFreq = 0;
            short endFreq = 0;
            short timeLength = 0;
            short amplitude = 0;
            bool validSettings = false;

            byte[] chirpConfigHeader = { 0x43, 0x43, 0x00 };
            byte[] configData = new byte[0];

            while (!validSettings)
            {
                try
                {
                    startFreq = short.Parse(await DisplayPromptAsync("Chirp Configuration", "Starting Frequency in Hz (range: 10 - 15000Hz)"));
                    if (startFreq < 10 || startFreq > 15000)
                    {
                        throw new InvalidDataException();
                    }

                    endFreq = short.Parse(await DisplayPromptAsync("Chirp Configuration", "Ending Frequency in Hz (range: 10 - 15000Hz)"));
                    if (endFreq < 10 || endFreq > 15000)
                    {
                        throw new InvalidDataException();
                    }

                    timeLength = short.Parse(await DisplayPromptAsync("Chirp Configuration", "Time length in ms (range: 10 - 200ms)"));
                    if (timeLength < 10 || timeLength > 200)
                    {
                        throw new InvalidDataException();
                    }

                    amplitude = short.Parse(await DisplayPromptAsync("Chirp Configuration", "Amplitude (range: 1 - 2500)"));
                    if (amplitude < 1 || amplitude > 2500)
                    {
                        throw new InvalidDataException();
                    }

                    configData = BitConverter.GetBytes(startFreq)
                        .Concat(BitConverter.GetBytes(endFreq))
                        .Concat(BitConverter.GetBytes(amplitude))
                        .Concat(BitConverter.GetBytes(timeLength))
                        .ToArray();
                    validSettings = true;
                }
                catch
                {
                    await DisplayAlert("Error", "Invalid Settings; please try again.", "OK");
                    continue;
                }
            }

            return chirpConfigHeader.Concat(configData).ToArray();
        }

        private async Task<byte[]> GetMicConfig()
        {
            short timeDelay = 0;
            short timeSpan = 0;
            bool validSettings = false;

            byte[] micConfigHeader = { 0x4d, 0x43, 0x00 };
            byte[] configData = new byte[0];

            while (!validSettings)
            {
                try
                {
                    timeDelay = short.Parse(await DisplayPromptAsync("Mic Configuration", "Time delay from when the chirp plays in ms (range: 0 - 800)"));
                    if (timeDelay < 0 || timeDelay > 800)
                    {
                        throw new InvalidDataException();
                    }

                    timeSpan = short.Parse(await DisplayPromptAsync("Mic Configuration", $"Length of recording in ms including time delay (range: {timeDelay} - 1000)"));
                    if (timeSpan < timeDelay || timeSpan > 1000)
                    {
                        throw new InvalidDataException();
                    }

                    configData = BitConverter.GetBytes(timeDelay)
                        .Concat(BitConverter.GetBytes(timeSpan))
                        .ToArray();
                    validSettings = true;
                }
                catch
                {
                    await DisplayAlert("Error", "Invalid Settings; please try again.", "OK");
                    continue;
                }
            }

            return micConfigHeader.Concat(configData).ToArray();
        }
    }
}
