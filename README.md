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

## Examples

Check out the 'Scene UI' Sample in Unity Package manager.

1. Open your Unity project and navigate to `Window` > `Package Manager`.
2. Select the installed Lottie Animation package on the left
3. Fold out the Samples section
4. Click 'Import' button on the Scene UI sample
5. After importing it will ask you to copy the sample animations to the Assets/StreaminAssets folder
6. Click 'Copy' button in the 'Missing lottie animations' dialog
7. Open the 'SampleScene' from the sample in 'Assets/Samples/Lottie Animation/[version]/Scene UI/Scenes/' folder
8. Hit play in Unity editor

https://user-images.githubusercontent.com/5675979/144131059-94285a28-61cf-44cf-94c9-9868923ed950.mp4

## Support

If you encounter any issues or have questions, please [create an issue](https://github.com/gindemit/unity-rlottie/issues/new) on GitHub.

## Credits

This plugin is built on top of Samsung's [rlottie](https://github.com/Samsung/rlottie) library, which is used internally for Lottie animation rendering.

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.
