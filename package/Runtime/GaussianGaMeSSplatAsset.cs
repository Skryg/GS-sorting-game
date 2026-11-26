// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using UnityEngine;

namespace GaussianSplatting.Runtime
{
    public class GaussianGaMeSSplatAsset : GaussianSplatAsset, IGaMeSGaussianSplatAsset
    {
        [SerializeField] string m_ObjPath;
        [SerializeField] int m_NumberOfSplatsPerFace;
        [SerializeField] TextAsset m_AlphaData;
        [SerializeField] TextAsset m_ScaleData;
        [SerializeField] string m_PointCloudPath;
        [SerializeField] bool m_useMeshLeftHandedCS;
        // Asset splat index -> GaMeS face splat index (int32 per splat). Imported data is Morton-reordered,
        // so this links it back to the mesh faces. Baked by Tools > Gaussian Splats > Bake GaMeS Splat Order.
        [SerializeField] TextAsset m_SplatOrderData;

        public string objPath => m_ObjPath;
        public int numberOfSplatsPerFace => m_NumberOfSplatsPerFace;
        public string pointCloudPath => m_PointCloudPath;
        public TextAsset alphaData => m_AlphaData;
        public TextAsset scaleData => m_ScaleData;

        public bool useMeshLeftHandedCS => m_useMeshLeftHandedCS;
        public TextAsset splatOrderData => m_SplatOrderData;

        public void SetSplatOrderData(TextAsset data)
        {
            m_SplatOrderData = data;
        }

        public void Initialize(int splats, VectorFormat formatPos, VectorFormat formatScale, ColorFormat formatColor, SHFormat formatSh, Vector3 bMin, Vector3 bMax, CameraInfo[] cameraInfos, string pointCloudPath, bool useMeshLeftHandedCS)
        {
            m_SplatCount = splats;
            m_FormatVersion = kCurrentVersion;
            m_PosFormat = formatPos;
            m_ScaleFormat = formatScale;
            m_ColorFormat = formatColor;
            m_SHFormat = formatSh;
            m_Cameras = cameraInfos;
            m_BoundsMin = bMin;
            m_BoundsMax = bMax;
            m_PointCloudPath = pointCloudPath;
            m_useMeshLeftHandedCS = useMeshLeftHandedCS;
        }


        public void SetNumberOfSplatsPerFace(int numberOfSplats)
        {
            m_NumberOfSplatsPerFace = numberOfSplats;
        }

        public void SetAssetFiles(TextAsset dataChunk, TextAsset dataPos, TextAsset dataOther, TextAsset dataColor, TextAsset dataSh, TextAsset dataAlpha = null, TextAsset dataScale = null)
        {
            m_ChunkData = dataChunk;
            m_PosData = dataPos;
            m_AlphaData = dataAlpha;
            m_ScaleData = dataScale;
            m_OtherData = dataOther;
            m_ColorData = dataColor;
            m_SHData = dataSh;
        }
        public void SetObjPath(string path)
        {
            m_ObjPath = path;
        }

    }
}
