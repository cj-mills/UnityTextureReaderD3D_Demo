using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using CJM.UnityTextureReaderD3D;

public class TestPixelReadback : MonoBehaviour
{

    // The screen object in the scene
    [Tooltip("Screen object in the scene")]
    public Transform screen;

    public enum PixelReadbackOptions
    {
        NativeTexturePtr,
        AsynGPUReadback,
        ReadPixels
    }

    [SerializeField]
    private PixelReadbackOptions readbackOption;


    // The MeshRenderer component attached to the screen object
    private MeshRenderer screenRenderer;

    // The dimensions of the test image
    private Vector2Int imageDims;

    // The texture of the test image
    private Texture imageTexture;

    // The RenderTexture used as input for the machine learning model
    private RenderTexture inputTexture;

    // A Texture2D used to store a modified version of the inputTexture
    private Texture2D modifiedTexture;

    // The width of the inputTexture in pixels
    private int width;

    // The height of the inputTexture in pixels
    private int height;

    // The number of bytes per pixel for the inputTexture
    private int numBytesPerPixel = 4;

    // The total size of the pixel data in bytes
    private int pixelDataSize;


    private ITexturePixelDataReaderD3D _texturePixelDataReaderD3D;


    /// <summary>
    /// Resizes and positions a screen object in the scene.
    /// </summary>
    /// <param name="screen">The screen object to be resized and positioned.</param>
    /// <param name="imageDims">The dimensions of the image to be displayed on the screen.</param>
    private void InitializeScreen(Transform screen, Vector2Int imageDims)
    {
        // Reset the rotation of the screen object
        screen.rotation = Quaternion.Euler(0, 0, 0);

        // Set the scale of the screen object to match the dimensions of the image
        screen.localScale = new Vector3(imageDims.x, imageDims.y, 1f);

        // Position the screen object in the center of the screen with a z-value of 1
        screen.position = new Vector3(imageDims.x / 2, imageDims.y / 2, 1);
    }


    /// <summary>
    /// Resizes and positions the main camera in the scene based on a screen object's dimensions.
    /// </summary>
    /// <param name="screenDims">The dimensions of the screen object.</param>
    /// <param name="cameraName">The name of the camera GameObject to resize and position.</param>
    private void InitializeCamera(Vector2Int screenDims, string cameraName = "Main Camera")
    {
        // Find the GameObject for the specified camera name
        GameObject camera = GameObject.Find(cameraName);

        // Adjust the position of the camera to center it on the screen object
        camera.transform.position = new Vector3(screenDims.x / 2, screenDims.y / 2, -10f);

        // Set the camera to render objects with no perspective
        camera.GetComponent<Camera>().orthographic = true;

        // Adjust the size of the camera to match the height of the screen object
        camera.GetComponent<Camera>().orthographicSize = screenDims.y / 2;
    }


    // Start is called before the first frame update
    void Start()
    {
        // Get the MeshRenderer component attached to the screen object
        screenRenderer = screen.gameObject.GetComponent<MeshRenderer>();

        // Get the source image texture and dimensions
        imageTexture = screenRenderer.material.mainTexture;
        imageDims = new Vector2Int(imageTexture.width, imageTexture.height);
        Debug.Log($"Image Dims: {imageDims}");

        // Create the input texture with the calculated input dimensions
        inputTexture = RenderTexture.GetTemporary(imageDims.x, imageDims.y, 24, RenderTextureFormat.Default);

        // Copy the source texture into the input texture
        Graphics.Blit(imageTexture, inputTexture);

        // Store input texture dimensions
        width = inputTexture.width;
        height = inputTexture.height;

        // Calculate the total size of the pixel data
        pixelDataSize = width * height * numBytesPerPixel;

        // Resize and position the screen object
        InitializeScreen(screen, imageDims);

        // Resize and position the main camera
        InitializeCamera(imageDims);

        // Create a modified texture from the input texture and apply it to the screen
        if (screenRenderer != null && screenRenderer.material != null)
        {
            if (inputTexture != null)
            {
                modifiedTexture = new Texture2D(inputTexture.width, inputTexture.height, TextureFormat.RGBA32, false);
                Graphics.CopyTexture(inputTexture, modifiedTexture);
                screenRenderer.material.mainTexture = modifiedTexture;
            }
        }

        Debug.Log(SystemInfo.graphicsDeviceType);


        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D11)
        {
            _texturePixelDataReaderD3D = new Direct3D11TexturePixelDataReader();
        }
        else if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12)
        {
            _texturePixelDataReaderD3D = new Direct3D12TexturePixelDataReader();
        }
    }


    /// <summary>
    /// Called once AsyncGPUReadback has been completed
    /// </summary>
    /// <param name="request"></param>
    private void OnCompleteReadback(AsyncGPUReadbackRequest request)
    {
        if (request.hasError)
        {
            Debug.Log("GPU readback error detected.");
            return;
        }

        // Make sure the Texture2D is not null
        if (modifiedTexture)
        {
            // Fill Texture2D with raw data from the AsyncGPUReadbackRequest
            modifiedTexture.LoadRawTextureData(request.GetData<uint>());
            // Apply changes to Textur2D
            modifiedTexture.Apply();
        }
    }


    // Update is called once per frame
    void Update()
    {
        // Check that modifiedTexture, screenRenderer, and screenRenderer.material are not null
        if (modifiedTexture != null && screenRenderer != null && screenRenderer.material != null)
        {

            if (readbackOption == PixelReadbackOptions.NativeTexturePtr)
            {
                // Get a native pointer to the input texture and retrieve the pixel data from it
                IntPtr nativeTexturePtr = inputTexture.GetNativeTexturePtr();
                IntPtr pixelDataPtr = _texturePixelDataReaderD3D.GetPixelDataFromTexture(nativeTexturePtr);

                if (pixelDataPtr != IntPtr.Zero)
                {
                    modifiedTexture.LoadRawTextureData(pixelDataPtr, pixelDataSize);
                    modifiedTexture.Apply();
                }
                else
                {
                    Debug.Log("Null pixelDataPtr");
                }

                _texturePixelDataReaderD3D.FreePixelData(pixelDataPtr);
            }

            if (readbackOption == PixelReadbackOptions.AsynGPUReadback)
            {
                AsyncGPUReadback.Request(inputTexture, 0, TextureFormat.RGBA32, OnCompleteReadback);
            }

            if (readbackOption == PixelReadbackOptions.ReadPixels)
            {
                RenderTexture.active = inputTexture;
                modifiedTexture.ReadPixels(new Rect(0, 0, inputTexture.width, inputTexture.height), 0, 0);
                modifiedTexture.Apply();
            }
        }
    }


    // Clean up resources when the object is disabled
    private void OnDisable()
    {
        // Release the temporary input texture
        RenderTexture.ReleaseTemporary(inputTexture);
    }
}
