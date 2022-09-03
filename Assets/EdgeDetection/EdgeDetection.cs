using UnityEngine;

public class EdgeDetection : MonoBehaviour
{
    // Different filtering modes
    public enum EdgeDetectionMode
    {
        Depth = 0,
        Color = 1,
        Normal = 2,
        Custom = 3
    }

    // Boolean that triggers or disables the effect
    public bool enableFilter = false;

    enum SubFilters
    {
        Smooth = 4,
        Gradient = 5,
        NonMax = 6,
        Hysteresis = 7,
        SoftSmooth = 8,
        CustomColorRestore = 9,
        Bloom = 10,
        CelShading = 11
    }

    public bool edgeFilterMode = false;
    
    [Range(0.0f, 1.0f)]
    public float highTreshold = 0.3f;
    [Range(0.0f, 1.0f)]
    public float lowTreshold = 0.1f;
    
    // Boolean that turns Cel shading ON (Enable filter must be ON too)
    public bool celShading = false;

    // Detection mode for the edges
    public EdgeDetectionMode detectionMode = EdgeDetectionMode.Color;

    // Do not modify (compute shader used for the modification)
    public ComputeShader edgeDetection = null;

    void OnPreRender()
    {
        Camera cam = GetComponent<Camera>();
        if (enableFilter && edgeDetection != null)
            cam.depthTextureMode = DepthTextureMode.DepthNormals;
        else
            cam.depthTextureMode = DepthTextureMode.Depth;
    }

    //Run kernel with kernelID and copies results to src
    //src has to be set before calling this and freed after same for edgeBuffer
    //Other values has to be set manually
    void RunKernel(int kernelID, RenderTexture src, RenderTexture edgeBuffer, int tileX, int tileY, bool save=true)
    {
        edgeDetection.SetTexture(kernelID, "_ColorEdgeBuffer", src);
        edgeDetection.SetTexture(kernelID, "_EdgesBufferRW", edgeBuffer);
        edgeDetection.Dispatch(kernelID, tileX, tileY, 1);
        if (save)
        {
            RenderTexture.ReleaseTemporary(src); //Here we release texture to avoid stacking
            Graphics.Blit(edgeBuffer, src); //save modifications into source
        }
    }

    //Canny Edge detector using 5x5 Sobel Filter
    void CannyEdgeDetector(RenderTexture src, RenderTexture edgeBuffer, int tileX, int tileY)
    {
        //Smooth
        RunKernel((int) SubFilters.Smooth, src, edgeBuffer, tileX, tileY);
            
        //Gradient
        RunKernel((int) SubFilters.Gradient, src, edgeBuffer, tileX, tileY);

        //Non Max Pass
        RunKernel((int) SubFilters.NonMax, src, edgeBuffer, tileX, tileY);
            
        //Double thresholding for weak and strong values
        RunKernel((int) SubFilters.Hysteresis, src, edgeBuffer, tileX, tileY);
        RenderTexture.ReleaseTemporary(src);//Here we release texture to avoid stacking
    }

    void CustomEffect(RenderTexture src, RenderTexture edgeBuffer, RenderTexture blur, int tileX, int tileY)
    {
        //Apply colors from scene to edges
        RunKernel((int) SubFilters.CustomColorRestore, src, edgeBuffer, tileX, tileY);
        
        //Apply colors from scene to blurred edges
        RunKernel((int) SubFilters.CustomColorRestore, blur, edgeBuffer, tileX, tileY);
        
        //Bloom Effect
        edgeDetection.SetFloat("time", Time.time);
        edgeDetection.SetTexture((int) SubFilters.Bloom, "_BlurredEdgeBuffer", blur);
        RunKernel((int) SubFilters.Bloom, src, edgeBuffer, tileX, tileY);

        RenderTexture.ReleaseTemporary(src);//Here we release texture to avoid stacking
        RenderTexture.ReleaseTemporary(blur);//Here we release texture to avoid stacking
    }

    void OnRenderImage(RenderTexture src, RenderTexture dest)
    {
        if (enableFilter && edgeDetection != null)
        {
            // Request the render texture we will be needing
            RenderTextureDescriptor rtD = new RenderTextureDescriptor();
            rtD.width = src.width;
            rtD.height = src.height;
            rtD.volumeDepth = 1;
            rtD.msaaSamples = 1;
            rtD.dimension = UnityEngine.Rendering.TextureDimension.Tex2D;
            rtD.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;
            rtD.enableRandomWrite = true;
            RenderTexture edgeBuffer = RenderTexture.GetTemporary(rtD);

            // Run the edge detection kernel
            int tileX = (src.width + 7) / 8;
            int tileY = (src.height + 7) / 8;
            int kernelID = (int)detectionMode;
            edgeDetection.SetTexture(kernelID, "_CameraColorBuffer", src);
            edgeDetection.SetTexture(kernelID, "_EdgesBufferRW", edgeBuffer);
            edgeDetection.SetInt("maxX", src.width);
            edgeDetection.SetInt("maxY", src.height);
            edgeDetection.SetFloat("weakTh", lowTreshold);
            edgeDetection.SetFloat("strongTh", highTreshold);
            edgeDetection.Dispatch(kernelID, tileX, tileY, 1);
            

            if (edgeFilterMode)
            {
                //This will be used as to store source after each operation
                rtD.enableRandomWrite = false;
                RenderTexture tmpSrc = RenderTexture.GetTemporary(rtD);
                Graphics.Blit(edgeBuffer, tmpSrc);//save modifications into tmp source

                //Detecting edges using Canny edge detector
                CannyEdgeDetector(tmpSrc, edgeBuffer, tileX, tileY);

                //Apply custom effect to Custom edge output
                if (detectionMode == EdgeDetectionMode.Custom && !celShading)
                {

                    //Post processing Output
                    Graphics.Blit(edgeBuffer, tmpSrc); //save modifications into source
                    RunKernel((int) SubFilters.SoftSmooth, tmpSrc, edgeBuffer, tileX, tileY, false);
                    
                    //Original output image is needed for bloom effect so blur is stored here
                    RenderTextureDescriptor rtDblur = new RenderTextureDescriptor(rtD.width, rtD.height);
                    rtDblur.volumeDepth = 1;
                    rtDblur.msaaSamples = 1;
                    rtDblur.dimension = UnityEngine.Rendering.TextureDimension.Tex2D;
                    rtDblur.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;
                    rtDblur.enableRandomWrite = false;
                    RenderTexture tmpBlur = RenderTexture.GetTemporary(rtDblur);
                    Graphics.Blit(edgeBuffer, tmpBlur, new Vector2(1.0f, -1.0f),
                        new Vector2(0.0f, 1.0f));

                    CustomEffect(tmpSrc, edgeBuffer, tmpBlur, tileX, tileY);
                }
                else
                {
                    if (celShading)
                    {
                        Graphics.Blit(edgeBuffer, tmpSrc); //save modifications into source
                        RunKernel((int) SubFilters.SoftSmooth, tmpSrc, edgeBuffer, tileX, tileY);

                        edgeDetection.SetTexture((int) SubFilters.CelShading, "_BlurredEdgeBuffer", tmpSrc);
                        RunKernel((int) SubFilters.CelShading, src, edgeBuffer, tileX, tileY, false);
                    }
                    RenderTexture.ReleaseTemporary(tmpSrc);
                }
            }

            // Copy into the Screen
            Graphics.Blit(edgeBuffer, dest);
            RenderTexture.ReleaseTemporary(edgeBuffer);
        }
        else
        {
            // Copy into the Screen
            Graphics.Blit(src, dest);
        }
    }
}