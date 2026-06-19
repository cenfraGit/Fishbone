using Dock.Model.Mvvm.Controls;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.ComponentModel;
using SpineIDE.Models.Messages;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Platform;
using System;

namespace SpineIDE.Panels;

public partial class ImagePreviewVM : Tool, IRecipient<MessageVariableSelected>
{
    // --------------------------------------------------------------------------------
    // fields and properties
    // --------------------------------------------------------------------------------

    private Bitmap? _displayedImage;
    public Bitmap? DisplayedImage
    {
        get => _displayedImage;
        set
        {
            _displayedImage?.Dispose();
            SetProperty(ref _displayedImage, value);
        }
    }

    // --------------------------------------------------------------------------------
    // constructor
    // --------------------------------------------------------------------------------

    public ImagePreviewVM()
    {
        WeakReferenceMessenger.Default.Register(this);
    }

    // --------------------------------------------------------------------------------
    // methods
    // --------------------------------------------------------------------------------

    public async void Receive(MessageVariableSelected m)
    {
        try
        {
            UpdateView(m.Name, m.Value);
        }
        catch { } // not image variable
    }

    private void UpdateView(string name, dynamic? matrix)
    {
        this.Title = "Image Preview";
        DisplayedImage = null;

        if (matrix == null) return;

        int width = matrix.Columns;
        int height = matrix.Rows;
        int channels = matrix.Channels;

        PixelFormat format;
        if (channels == 1)
            format = PixelFormats.Gray8;
        else if (channels == 3)
            format = PixelFormats.Rgb24;
        else
            format = PixelFormats.Bgra8888;

        var writeableBitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            format,
            AlphaFormat.Opaque);

        using (var lockedBuffer = writeableBitmap.Lock())
        {
            var rawDataArray = matrix.Data;
            Type elementType = rawDataArray.GetType().GetElementType();
            int elementSize = Marshal.SizeOf(elementType);
            int sourceRowByteCount = width * channels * elementSize;

            var handle = GCHandle.Alloc(rawDataArray, GCHandleType.Pinned);
            try
            {
                IntPtr basePtr = handle.AddrOfPinnedObject();
                unsafe{
                    byte* srcPtr = (byte*)basePtr.ToPointer();
                    byte* destPtr = (byte*)lockedBuffer.Address.ToPointer();

                    for (int y = 0; y < height; y++)
                    {
                        byte* currentSrcRow = srcPtr + (y * sourceRowByteCount);
                        byte* currentDestRow = destPtr + (y * lockedBuffer.RowBytes);

                        Buffer.MemoryCopy(
                            currentSrcRow,
                            currentDestRow,
                            lockedBuffer.RowBytes,
                            sourceRowByteCount);
                    }
                }
            }
            finally
            {
                handle.Free();
            }
        }

        DisplayedImage = writeableBitmap;
        this.Title = $"Image Preview: {name}";
    }
}