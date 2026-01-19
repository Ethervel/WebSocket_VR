using System;

namespace VRMeeting.Sharing
{
    /// <summary>
    /// Network data classes for file presentation feature.
    /// Follows same patterns as ScreenShareData.cs
    /// </summary>

    [Serializable]
    public class FilePresentStartData
    {
        public string roomId;
        public string whiteboardId;
        public string fileId;
        public string fileName;
        public string fileExtension;
        public string presenterId;
        public string presenterName;
        public int totalPages;
        public int currentPage;
    }

    [Serializable]
    public class FilePresentPageData
    {
        public string roomId;
        public string whiteboardId;
        public string fileId;
        public int pageNumber;
        public string imageDataBase64;
        public int width;
        public int height;
        public long timestamp;
    }

    [Serializable]
    public class FilePresentNavigateData
    {
        public string roomId;
        public string whiteboardId;
        public string fileId;
        public int newPageNumber;
        public string presenterId;
    }

    [Serializable]
    public class FilePresentStopData
    {
        public string roomId;
        public string whiteboardId;
        public string fileId;
        public string presenterId;
    }

    [Serializable]
    public class FilePresentRequestData
    {
        public string roomId;
        public string whiteboardId;
        public string requesterId;
    }

    [Serializable]
    public class FilePresentStateData
    {
        public string roomId;
        public string whiteboardId;
        public string targetId;
        public bool isPresenting;
        public string fileId;
        public string fileName;
        public string presenterId;
        public string presenterName;
        public int totalPages;
        public int currentPage;
        public string currentPageImageBase64;
    }

    // PDF conversion request/response (client <-> server)
    [Serializable]
    public class PdfConvertRequestData
    {
        public string roomId;
        public string fileId;
        public string fileDataBase64;
        public string requesterId;
    }

    [Serializable]
    public class PdfConvertResponseData
    {
        public string roomId;
        public string fileId;
        public string targetId;
        public int totalPages;
        public bool success;
        public string error;
    }

    [Serializable]
    public class PdfPageRequestData
    {
        public string roomId;
        public string fileId;
        public int pageNumber;
        public string requesterId;
    }

    [Serializable]
    public class PdfPageResponseData
    {
        public string roomId;
        public string fileId;
        public string targetId;
        public int pageNumber;
        public string imageDataBase64;
        public int width;
        public int height;
    }
}
