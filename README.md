# Unity Lottie Animation Plugin
![GitHub release](https://img.shields.io/github/release/gindemit/unity-rlottie.svg)
![Unity version](https://img.shields.io/badge/unity-2019.4%2B-green.svg)
![License](https://img.shields.io/github/license/gindemit/unity-rlottie.svg)

Welcome to the **Unity Lottie Animation** plugin! This Unity library enables you to play Lottie animations as Texture2D in your Unity projects. It's a powerful and easy-to-use solution that leverages Samsung's [rlottie](https://github.com/Samsung/rlottie) library to bring smooth Lottie animations to your games and applications.

Lottie loads and renders animations and vectors exported in the bodymovin JSON format. Bodymovin JSON can be created and exported from After Effects with bodymovin, Sketch with Lottie Sketch Export, and from Haiku.

For the first time, designers can create and ship beautiful animations without an engineer painstakingly recreating it by hand. Since the animation is backed by JSON they are extremely small in size but can be large in complexity!

## Features

* Play Lottie animations as Texture2D in Unity
* Easy integration and usage
* High performance and low memory footprint
* Uses Samsung's rlottie library internally
* Compatible with Unity 2019.4 and later

## Supported Platforms

* Android
* iOS
* Windows
* OSX
* Linux

## Installation

To install the Unity Lottie Animation to Texture2D plugin using the Unity Package Manager, follow these steps:

1. Open your Unity project and navigate to `Window` > `Package Manager`.
2. Click the `+` button in the top-left corner and select `Add package from git URL...`.
3. Enter the following URL and click `Add`:
   ```
   https://github.com/gindemit/unity-rlottie.git?path=/unity/RLottieUnity/Assets/LottiePlugin/#0.2.0
   ```

## Quick Start

The `LottiePlugin.UI` namespace includes a `AnimatedImage` class which allows you to use Lottie animations as `RawImage` in your Unity game.
Here are the steps to use the `AnimatedImage` class in your Unity project:

1. **Import the Class:** First, make sure the `AnimatedImage` class is included in your Unity project.

2. **Create an RawImage:** In your Unity scene, create a new RawImage UGUI game object and attach the `AnimatedImage` script to it.

3. **Assign Parameters:** In the `AnimatedImage` component, you will see several parameters:

    - **Animation Json**: A `TextAsset` representing the JSON data for your Lottie animation. You can drag and drop it from your Assets folder.
    - **Texture Width**: Defines the width of the texture for the animation. Increasing this value will improve the quality of the animation by increasing the number of pixels, but it may also impact performance, especially on lower-end hardware.
    - **Texture Height**: Defines the height of the texture for the animation. Similar to Texture Width, increasing this value enhances the quality of the animation by adding more pixels, but it could potentially degrade performance on less powerful devices.
    - **Animation Speed**: Speed at which animation plays. 1 is normal speed.
    - **Play On Awake**: If checked, the animation will start playing as soon as the scene starts.
    - **Loop**: If checked, the animation will keep playing in a loop.

    Fill these in according to your needs.

4. **Play/Stop the Animation:** You can use the `Play()` and `Stop()` methods to control the animation at runtime. You can call these from any other script.

    ```csharp
    // Get a reference to the AnimatedImage
    AnimatedImage animatedImage = gameObject.GetComponent<AnimatedImage>();

    // Play the animation
    animatedImage.Play();

    // Stop the animation
    animatedImage.Stop();
    ```
And that's it! You are now able to use Lottie animations in your Unity projects using the `AnimatedImage` class.

## Example

Here's an example of how to use the `AnimatedImage` class:

```csharp
// Attach this script to a GameObject with AnimatedImage component

using UnityEngine;
using LottiePlugin.UI;

public class AnimationController : MonoBehaviour
{
    private AnimatedImage animatedImage;

    private void Awake()
    {
        animatedImage = GetComponent<AnimatedImage>();
    }

    private void Start()
    {
        animatedImage.Play(); // Start the animation
    }

    private void OnDisable()
    {
        animatedImage.Stop(); // Stop the animation
    }
}
```
This script, when attached to the same GameObject as the `AnimatedImage` component, will start the animation when the game starts and stop it when the GameObject is disabled.

## Other Examples

Check out the 'Scene UI' Sample in Unity Package manager.

1. Open your Unity project and navigate to `Window` > `Package Manager`.
2. Select the installed Lottie Animation package on the left
3. Fold out the Samples section
4. Click 'Import' button on the Scene UI sample
5. After importing it will ask you to copy the sample animations to the Assets/StreaminAssets folder
6. Click 'Copy' button in the 'Missing lottie animations' dialog
7. Open the 'SampleScene' from the sample in 'Assets/Samples/Lottie Animation/[version]/Scene UI/Scenes/' folder
8. Hit play in Unity editor

https://github.com/gindemit/unity-rlottie/assets/5675979/3301d00e-fc9e-49c0-bc7f-7408f1f72ce4

## Support

If you encounter any issues or have questions, please [create an issue](https://github.com/gindemit/unity-rlottie/issues/new) on GitHub.

## Credits

This plugin is built on top of Samsung's [rlottie](https://github.com/Samsung/rlottie) library, which is used internally for Lottie animation rendering.

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.
