using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;
using VoltTray.Core.Models;
using VoltTray.Infrastructure.Hid;
using VoltTray.Tests.ObjectMothers;
using Xunit;

namespace VoltTray.Tests;

public class Win32HidUpsPortTests
{
    private const int BlockedReadSimulationMs = 1500;
    private const int DisconnectSlaBudgetMs = 100;

    [Fact]
    public async Task Disconnect_DuringBlockedRead_CompletesUnder100MsAndCancelsIo()
    {
        var connection = new BlockingFakeHidConnection();
        var transport = new FakeHidTransport(connection);
        var scanner = new FakeHidDeviceScanner(CreateMatchingDescriptor(@"\\?\HID#VID_0665&PID_5161#port-1"));

        using var port = new Win32HidUpsPort(
            preferredDevicePath: null,
            allowAutoDiscovery: true,
            estimator: null,
            deviceScanner: scanner,
            transport: transport);

        Assert.True(port.TryConnect());
        Assert.True(port.IsConnected);

        Task<UpsTelemetry?> readTask = port.ReadTelemetryAsync();
        await connection.WaitForReadEntryAsync();

        var stopwatch = Stopwatch.StartNew();
        port.Disconnect();
        stopwatch.Stop();

        UpsTelemetry? telemetry = await readTask;

        Assert.True(
            stopwatch.ElapsedMilliseconds < DisconnectSlaBudgetMs,
            $"Disconnect took {stopwatch.ElapsedMilliseconds} ms (limit < {DisconnectSlaBudgetMs} ms).");
        Assert.True(connection.WasCancelPendingIoCalled);
        Assert.False(port.IsConnected);
        Assert.Null(port.ActiveDevicePath);
        Assert.Null(telemetry);
    }

    [Fact]
    public async Task Disconnect_WhenBlockedReadReturnsStaleFrame_DoesNotReturnOrLeakTelemetry()
    {
        var connection = new StaleFrameOnCancelHidConnection(VoltronicFrameMother.CreateSelfTestFrame());
        var transport = new FakeHidTransport(connection);
        var scanner = new FakeHidDeviceScanner(CreateMatchingDescriptor(@"\\?\HID#VID_0665&PID_5161#port-2"));

        using var port = new Win32HidUpsPort(
            preferredDevicePath: null,
            allowAutoDiscovery: true,
            estimator: null,
            deviceScanner: scanner,
            transport: transport);

        Assert.True(port.TryConnect());

        Task<UpsTelemetry?> readTask = port.ReadTelemetryAsync();
        await connection.WaitForReadEntryAsync();

        port.Disconnect();
        UpsTelemetry? staleResult = await readTask;

        Assert.Null(staleResult);
        Assert.False(port.IsConnected);
    }

    [Fact]
    public void TryConnect_WithAutoDiscoveryEnabled_UsesHidDeviceScannerResult()
    {
        const string discoveredPath = @"\\?\HID#VID_0665&PID_5161#dynamic-port-7";
        var connection = new ImmediateFrameHidConnection(VoltronicFrameMother.GoldenFrame);
        var transport = new FakeHidTransport(connection);
        var scanner = new FakeHidDeviceScanner(CreateMatchingDescriptor(discoveredPath));

        using var port = new Win32HidUpsPort(
            preferredDevicePath: null,
            allowAutoDiscovery: true,
            estimator: null,
            deviceScanner: scanner,
            transport: transport);

        bool connected = port.TryConnect();

        Assert.True(connected);
        Assert.True(port.IsConnected);
        Assert.Equal(discoveredPath, port.ActiveDevicePath);
    }

    [Fact]
    public void TryConnect_WithAutoDiscoveryDisabledAndNoPreferredPath_ReturnsFalse()
    {
        var connection = new ImmediateFrameHidConnection(VoltronicFrameMother.GoldenFrame);
        var transport = new FakeHidTransport(connection);
        var scanner = new FakeHidDeviceScanner(CreateMatchingDescriptor(@"\\?\HID#VID_0665&PID_5161#ignored"));

        using var port = new Win32HidUpsPort(
            preferredDevicePath: null,
            allowAutoDiscovery: false,
            estimator: null,
            deviceScanner: scanner,
            transport: transport);

        bool connected = port.TryConnect();

        Assert.False(connected);
        Assert.False(port.IsConnected);
    }

    [Theory]
    [InlineData(-500, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(400, 400)]
    public void HidOverlappedIo_ClampTimeoutMs_PreventsNegativeInfiniteWrap(int inputTimeoutMs, int expectedTimeoutMs)
    {
        Assert.Equal(expectedTimeoutMs, HidOverlappedIo.ClampTimeoutMs(inputTimeoutMs));
    }

    [Fact]
    public void HidOverlappedIo_ReadReportWithInvalidHandleAndNegativeTimeout_ReturnsZeroWithoutHanging()
    {
        using var invalidHandle = new SafeFileHandle(IntPtr.Zero, ownsHandle: false);
        byte[] buffer = new byte[HidConstants.ReportLength];

        var stopwatch = Stopwatch.StartNew();
        int bytesRead = HidOverlappedIo.ReadReport(invalidHandle, buffer, timeoutMs: -500);
        stopwatch.Stop();

        Assert.Equal(0, bytesRead);
        Assert.True(stopwatch.ElapsedMilliseconds < DisconnectSlaBudgetMs);
    }

    [Fact]
    public void HidOverlappedIo_OnAlreadyDisposedHandle_ReturnsSafelyWithoutObjectDisposedException()
    {
        string tempFilePath = Path.Combine(Path.GetTempPath(), $"volttray-handle-test-{Guid.NewGuid():N}.tmp");
        SafeFileHandle disposedHandle = File.OpenHandle(
            tempFilePath,
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.ReadWrite,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        disposedHandle.Dispose();

        byte[] reportBuffer = new byte[HidConstants.ReportLength];

        Exception? cancelException = Record.Exception(() => HidOverlappedIo.CancelPendingIo(disposedHandle));
        int bytesRead = HidOverlappedIo.ReadReport(disposedHandle, reportBuffer, HidConstants.DefaultReadTimeoutMs);
        bool writeSucceeded = HidOverlappedIo.WriteReport(disposedHandle, reportBuffer, HidConstants.DefaultWriteTimeoutMs);

        Assert.Null(cancelException);
        Assert.Equal(0, bytesRead);
        Assert.False(writeSucceeded);
    }

    [Fact]
    public async Task HidOverlappedIo_ConcurrentHandleDisposalDuringRead_CompletesWithoutObjectDisposedException()
    {
        string pipeName = $"volttray-overlapped-test-{Guid.NewGuid():N}";
        using var serverPipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        Task waitForClientTask = serverPipe.WaitForConnectionAsync();
        using SafeFileHandle clientHandle = HidOverlappedIo.OpenDeviceHandle($@"\\.\pipe\{pipeName}");
        Assert.False(clientHandle.IsInvalid);
        await waitForClientTask;

        byte[] buffer = new byte[HidConstants.ReportLength];
        Task<int> readTask = Task.Run(() => HidOverlappedIo.ReadReport(clientHandle, buffer, timeoutMs: 250));

        await Task.Delay(30);
        HidOverlappedIo.CancelPendingIo(clientHandle);
        clientHandle.Dispose();

        int bytesRead = await readTask;
        Assert.Equal(0, bytesRead);
    }

    private static HidDeviceDescriptor CreateMatchingDescriptor(string devicePath)
    {
        return new HidDeviceDescriptor(
            devicePath,
            HidConstants.VendorId,
            HidConstants.ProductId,
            0x0100,
            "Forza",
            "NT-2401PRO",
            "(none)",
            HidConstants.ReportLength,
            HidConstants.ReportLength);
    }

    private sealed class FakeHidDeviceScanner : IHidDeviceScanner
    {
        private readonly IReadOnlyList<HidDeviceDescriptor> _devices;

        public FakeHidDeviceScanner(params HidDeviceDescriptor[] devices)
        {
            _devices = devices;
        }

        public IReadOnlyList<HidDeviceDescriptor> DiscoverDevices(bool includeAllHidDevices = false)
        {
            return _devices;
        }
    }

    private sealed class FakeHidTransport : IHidTransport
    {
        private readonly IHidConnection _connection;

        public FakeHidTransport(IHidConnection connection)
        {
            _connection = connection;
        }

        public IHidConnection? TryOpen(string devicePath)
        {
            return _connection;
        }
    }

    private sealed class BlockingFakeHidConnection : IHidConnection
    {
        private readonly TaskCompletionSource<bool> _readEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _cancelSignal = new(initialState: false);

        public bool IsOpen { get; private set; } = true;
        public bool WasCancelPendingIoCalled { get; private set; }

        public Task WaitForReadEntryAsync() => _readEntered.Task;

        public void CancelPendingIo()
        {
            WasCancelPendingIoCalled = true;
            _cancelSignal.Set();
        }

        public void DrainInputBuffer(int drainTimeoutMs)
        {
        }

        public bool WriteCommand(string command)
        {
            return IsOpen;
        }

        public int ReadReport(byte[] buffer, int timeoutMs)
        {
            _readEntered.TrySetResult(true);
            _cancelSignal.Wait(BlockedReadSimulationMs);
            return 0;
        }

        public void Dispose()
        {
            IsOpen = false;
            _cancelSignal.Set();
        }
    }

    private sealed class StaleFrameOnCancelHidConnection : IHidConnection
    {
        private readonly byte[] _frame;
        private readonly TaskCompletionSource<bool> _readEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _cancelSignal = new(initialState: false);

        public StaleFrameOnCancelHidConnection(byte[] frame)
        {
            _frame = frame;
        }

        public bool IsOpen { get; private set; } = true;

        public Task WaitForReadEntryAsync() => _readEntered.Task;

        public void CancelPendingIo()
        {
            _cancelSignal.Set();
        }

        public void DrainInputBuffer(int drainTimeoutMs)
        {
        }

        public bool WriteCommand(string command)
        {
            return IsOpen;
        }

        public int ReadReport(byte[] buffer, int timeoutMs)
        {
            _readEntered.TrySetResult(true);
            _cancelSignal.Wait(BlockedReadSimulationMs);
            buffer[0] = HidConstants.DefaultReportId;
            int copyCount = Math.Min(HidConstants.PayloadChunkLength, _frame.Length);
            Array.Copy(_frame, 0, buffer, HidConstants.ReportPayloadStartIndex, copyCount);
            return copyCount + 1;
        }

        public void Dispose()
        {
            IsOpen = false;
            _cancelSignal.Set();
        }
    }

    private sealed class ImmediateFrameHidConnection : IHidConnection
    {
        private readonly byte[] _frame;

        public ImmediateFrameHidConnection(byte[] frame)
        {
            _frame = frame;
        }

        public bool IsOpen { get; private set; } = true;

        public void CancelPendingIo()
        {
        }

        public void DrainInputBuffer(int drainTimeoutMs)
        {
        }

        public bool WriteCommand(string command)
        {
            return IsOpen;
        }

        public int ReadReport(byte[] buffer, int timeoutMs)
        {
            buffer[0] = HidConstants.DefaultReportId;
            int copyCount = Math.Min(HidConstants.PayloadChunkLength, _frame.Length);
            Array.Copy(_frame, 0, buffer, HidConstants.ReportPayloadStartIndex, copyCount);
            return copyCount + 1;
        }

        public void Dispose()
        {
            IsOpen = false;
        }
    }
}
