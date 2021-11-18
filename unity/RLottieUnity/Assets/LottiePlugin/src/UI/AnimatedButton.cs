using UnityEngine;
using UnityEngine.UI;

namespace LottiePlugin.UI
{
    public sealed class AnimatedButton : Button
    {
        [System.Serializable]
        public struct State
        {
            public string Name;
            public int FrameNumber;
            public bool StayHere;
        }

        internal TextAsset AnimationJson => _animationJson;
        internal uint TextureWidth => _textureWidth;
        internal uint TextureHeight => _textureHeight;

        internal Graphic Graphic => _graphic;
        internal State[] States => _states;

        private State _currentState;


        [SerializeField] private TextAsset _animationJson;
        [SerializeField] private uint _textureWidth;
        [SerializeField] private uint _textureHeight;
        [SerializeField] private Graphic _graphic;
        [SerializeField] private State[] _states;

    }
}
