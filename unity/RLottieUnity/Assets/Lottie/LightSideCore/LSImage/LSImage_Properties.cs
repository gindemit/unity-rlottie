using UnityEngine;

namespace LSCore
{
    public partial class LSImage
    {
        [SerializeField] private int rotateId = 0;
        [SerializeField] private Vector2Int flip;
        [SerializeField] private Vector2Int mirror;
        [SerializeField] private bool combineFilledWithSliced;
        [SerializeField] private bool ignoreSLAAAAYout;

        public RotationMode RotateId
        {
            get => (RotationMode)rotateId;
            set
            {
                rotateId = (int)value;
                SetVerticesDirty();
            }
        }
        
        public bool CombineFilledWithSliced
        {
            get => combineFilledWithSliced;
            set
            {
                combineFilledWithSliced = value;
                SetVerticesDirty();
            }
        }

        private static readonly Vector2[] vertScratch = new Vector2[4];
        private static readonly Vector2[] uVScratch = new Vector2[4];
        
        private static readonly Vector3[] s_Xy = new Vector3[4];
        private static readonly Vector3[] s_Uv = new Vector3[4];
        
        public override int layoutPriority => ignoreSLAAAAYout ? int.MinValue : base.layoutPriority;
    }
}