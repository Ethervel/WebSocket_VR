using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

/// <summary>
/// Capture de fenêtres Windows via API native
/// Windows uniquement (user32.dll, gdi32.dll)
/// </summary>
public static class WindowCapture
{
    #region Windows API

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
        IntPtr hdcSrc, int xSrc, int ySrc, uint rop);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines,
        byte[] lpvBits, ref BITMAPINFO lpbmi, uint uUsage);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    private const uint SRCCOPY = 0x00CC0020;
    private const uint PW_RENDERFULLCONTENT = 0x00000002;
    private const uint DIB_RGB_COLORS = 0;
    private const uint BI_RGB = 0;

    #endregion

    /// <summary>
    /// Info sur une fenêtre capturables
    /// </summary>
    public class WindowInfo
    {
        public IntPtr Handle;
        public string Title;
        public int Width;
        public int Height;

        public override string ToString() => Title;
    }

    /// <summary>
    /// Liste toutes les fenêtres visibles avec un titre
    /// </summary>
    public static List<WindowInfo> GetOpenWindows()
    {
        var windows = new List<WindowInfo>();
        IntPtr unityHandle = GetUnityWindowHandle();

        EnumWindows((hWnd, lParam) =>
        {
            // Ignorer les fenêtres invisibles
            if (!IsWindowVisible(hWnd))
                return true;

            // Ignorer la fenêtre Unity elle-même
            if (hWnd == unityHandle)
                return true;

            // Obtenir le titre
            int length = GetWindowTextLength(hWnd);
            if (length == 0)
                return true;

            StringBuilder sb = new StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            string title = sb.ToString();

            // Ignorer certaines fenêtres système
            if (string.IsNullOrWhiteSpace(title))
                return true;
            if (title == "Program Manager" || title == "Windows Input Experience")
                return true;

            // Obtenir les dimensions
            GetWindowRect(hWnd, out RECT rect);
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            // Ignorer les fenêtres trop petites
            if (width < 100 || height < 100)
                return true;

            windows.Add(new WindowInfo
            {
                Handle = hWnd,
                Title = title,
                Width = width,
                Height = height
            });

            return true;
        }, IntPtr.Zero);

        return windows;
    }

    /// <summary>
    /// Obtient le handle de la fenêtre Unity
    /// </summary>
    private static IntPtr GetUnityWindowHandle()
    {
        // Trouver la fenêtre Unity par son processus
        uint unityPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        IntPtr unityWindow = IntPtr.Zero;

        EnumWindows((hWnd, lParam) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == unityPid && IsWindowVisible(hWnd))
            {
                int length = GetWindowTextLength(hWnd);
                if (length > 0)
                {
                    unityWindow = hWnd;
                    return false; // Stop enumeration
                }
            }
            return true;
        }, IntPtr.Zero);

        return unityWindow;
    }

    /// <summary>
    /// Capture une fenêtre spécifique dans une Texture2D
    /// </summary>
    public static bool CaptureWindow(WindowInfo window, Texture2D targetTexture)
    {
        if (window == null || window.Handle == IntPtr.Zero)
            return false;

        try
        {
            // Obtenir les dimensions actuelles
            GetWindowRect(window.Handle, out RECT rect);
            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            if (width <= 0 || height <= 0)
                return false;

            // Obtenir le DC de la fenêtre
            IntPtr hdcWindow = GetWindowDC(window.Handle);
            if (hdcWindow == IntPtr.Zero)
                return false;

            // Créer un DC compatible et un bitmap
            IntPtr hdcMem = CreateCompatibleDC(hdcWindow);
            IntPtr hBitmap = CreateCompatibleBitmap(hdcWindow, width, height);
            IntPtr hOld = SelectObject(hdcMem, hBitmap);

            // Capturer la fenêtre avec PrintWindow (meilleur pour les fenêtres modernes)
            bool success = PrintWindow(window.Handle, hdcMem, PW_RENDERFULLCONTENT);

            if (!success)
            {
                // Fallback: BitBlt
                BitBlt(hdcMem, 0, 0, width, height, hdcWindow, 0, 0, SRCCOPY);
            }

            // Préparer la structure BITMAPINFO
            BITMAPINFO bmi = new BITMAPINFO();
            bmi.bmiHeader.biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER));
            bmi.bmiHeader.biWidth = width;
            bmi.bmiHeader.biHeight = -height; // Négatif pour top-down
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = BI_RGB;

            // Lire les pixels
            byte[] pixels = new byte[width * height * 4];
            GetDIBits(hdcMem, hBitmap, 0, (uint)height, pixels, ref bmi, DIB_RGB_COLORS);

            // Convertir BGRA -> RGBA et créer la texture
            Color32[] colors = new Color32[width * height];
            for (int i = 0; i < width * height; i++)
            {
                int idx = i * 4;
                colors[i] = new Color32(
                    pixels[idx + 2],  // R (était B)
                    pixels[idx + 1],  // G
                    pixels[idx + 0],  // B (était R)
                    255               // A
                );
            }

            // Redimensionner si nécessaire
            if (targetTexture.width != width || targetTexture.height != height)
            {
                targetTexture.Reinitialize(width, height);
            }

            targetTexture.SetPixels32(colors);
            targetTexture.Apply();

            // Cleanup
            SelectObject(hdcMem, hOld);
            DeleteObject(hBitmap);
            DeleteDC(hdcMem);
            ReleaseDC(window.Handle, hdcWindow);

            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[WindowCapture] Error: {e.Message}");
            return false;
        }
    }
}
