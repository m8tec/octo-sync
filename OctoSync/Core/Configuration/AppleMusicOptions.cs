namespace OctoSync.Core.Configuration;

public class AppleMusicOptions
{
    public string BaseUrl { get; set; } = "https://music.apple.com";
    public string CountryCode { get; set; } = "us";
    public bool EnableAnimatedCoverSync { get; set; } = true;
    public string AnimatedArtworkApiBaseUrl { get; set; } = "https://artwork.m8tec.top";
    public int AnimatedMinVariantResolution { get; set; } = 700;
    public int AnimatedWebpQuality { get; set; } = 30;
    public string FfmpegBinaryPath { get; set; } = "ffmpeg";
}