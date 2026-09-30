using System.ComponentModel;

namespace Fuse.Domain.Common;

public enum FileType
{
    [Description(".jpg,.png,.jpeg,.webp,.avif")]
    Image,
    [Description(".mp4,.mov,.avi,.wmv,.webm")]
    Video
}