using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarsGraphics.Models
{
    internal sealed class QualitySnapshot
    {
        public float ShadowDistance { get; set; }
        public int ShadowCascades { get; set; }
        public ShadowResolution ShadowResolution { get; set; }
        public float LodBias { get; set; }
        public int MaximumLodLevel { get; set; }
        public int PixelLightCount { get; set; }
        public bool RealtimeReflectionProbes { get; set; }
        public int AntiAliasing { get; set; }
        public int GlobalTextureMipmapLimit { get; set; }
        public int ShaderMaximumLod { get; set; }
        public AnisotropicFiltering AnisotropicFiltering { get; set; }
        public int VSyncCount { get; set; }
        public int TargetFrameRate { get; set; }
    }

    internal sealed class PipelineAssetSnapshot
    {
        public object Asset { get; }
        public Dictionary<string, object?> Values { get; } = new Dictionary<string, object?>();

        public PipelineAssetSnapshot(object asset)
        {
            Asset = asset;
        }
    }

    internal sealed class CameraSnapshot
    {
        public float FarClipPlane { get; set; }
        public bool LayerCullSpherical { get; set; }
        public bool UseOcclusionCulling { get; set; }
        public float[] LayerCullDistances { get; set; } = new float[32];
        public List<Camera> Stack { get; } = new List<Camera>();
    }

    internal sealed class LightSnapshot
    {
        public LightShadows Shadows { get; set; }
        public LightShadowResolution ShadowResolution { get; set; }
    }

    internal sealed class ReflectionProbeSnapshot
    {
        public bool Enabled { get; set; }
    }

    internal sealed class ComponentPropertySnapshot
    {
        public Dictionary<string, object?> Values { get; } = new Dictionary<string, object?>();
    }

    internal sealed class BehaviourSnapshot
    {
        public bool Enabled { get; set; }
    }

    internal sealed class RendererSnapshot
    {
        public Renderer? Renderer { get; set; }
        public bool Enabled { get; set; }
    }

    internal sealed class LodGroupSnapshot
    {
        public LODGroup? Group { get; set; }
    }

    internal sealed class RendererFeatureSnapshot
    {
        public UnityEngine.Rendering.Universal.ScriptableRendererFeature? Feature { get; set; }
        public bool Active { get; set; }
    }

    internal sealed class TerrainSnapshot
    {
        public Terrain? Terrain { get; set; }
        public bool DrawTreesAndFoliage { get; set; }
        public float DetailObjectDistance { get; set; }
    }
}


