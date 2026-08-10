namespace LottiePlugin
{
    /// <summary>
    /// Defines the method used for BGRA to RGBA color channel conversion on WebGL.
    /// </summary>
    public enum WebGLColorConversionMode
    {
        /// <summary>
        /// Use a GPU shader to swap B and R channels. This is the default and fastest option.
        /// The shader blits the source texture to a destination texture with swapped channels.
        /// </summary>
        Shader = 0,

        /// <summary>
        /// Use native C++ code to swap B and R channels on the CPU.
        /// This is a fallback option if the shader approach doesn't work on certain devices.
        /// </summary>
        Native = 1
    }

    /// <summary>
    /// Global settings for Lottie animations on WebGL platform.
    /// These settings control how BGRA to RGBA color conversion is performed.
    /// </summary>
    public static class LottieWebGLSettings
    {
        private static WebGLColorConversionMode s_ColorConversionMode = WebGLColorConversionMode.Shader;

        /// <summary>
        /// Gets or sets the color conversion mode for WebGL.
        /// Default is <see cref="WebGLColorConversionMode.Shader"/> which uses GPU-based conversion.
        /// </summary>
        /// <remarks>
        /// The rlottie library renders frames in BGRA format, but WebGL only supports RGBA textures.
        /// This setting controls how the color channel conversion is performed:
        /// <list type="bullet">
        ///   <item><description><see cref="WebGLColorConversionMode.Shader"/>: Uses a GPU shader to swap channels (faster, default)</description></item>
        ///   <item><description><see cref="WebGLColorConversionMode.Native"/>: Uses native C++ code on CPU (fallback)</description></item>
        /// </list>
        /// </remarks>
        public static WebGLColorConversionMode ColorConversionMode
        {
            get => s_ColorConversionMode;
            set => s_ColorConversionMode = value;
        }

        /// <summary>
        /// Returns true if shader-based color conversion should be used.
        /// </summary>
        public static bool UseShaderConversion => s_ColorConversionMode == WebGLColorConversionMode.Shader;

        /// <summary>
        /// Returns true if native C++ color conversion should be used.
        /// </summary>
        public static bool UseNativeConversion => s_ColorConversionMode == WebGLColorConversionMode.Native;
    }
}
