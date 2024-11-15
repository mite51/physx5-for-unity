using System.Linq;
using UnityEngine;

namespace PhysX5ForUnity
{
    [AddComponentMenu("PhysX 5/Renderers/PBD Particle System Fluid Diffuse Renderer")]
    public class PhysxPBDParticleSystemFluidDiffuseRenderer : PhysxPBDParticleSystemFluidRenderer
    {
        private void DiffuseColor()
        {
            ComputeBuffer oldColorBuffer;
            ComputeBuffer newColorBuffer;
            if (m_swapBuffersFlag)
            {
                oldColorBuffer = m_colorsBuffer;
                newColorBuffer = m_newColorsBuffer;
            }
            else
            {
                oldColorBuffer = m_newColorsBuffer;
                newColorBuffer = m_colorsBuffer;
            }
            m_swapBuffersFlag = !m_swapBuffersFlag;
            // Clear the grid
            int kernel = m_colorDiffusionShader.FindKernel("ClearGrid");
            m_colorDiffusionShader.SetBuffer(kernel, "grid", m_diffuseColorGridBuffer);
            m_colorDiffusionShader.Dispatch(kernel, Mathf.CeilToInt(m_diffuseColorGridSize.x * m_diffuseColorGridSize.y * m_diffuseColorGridSize.z * m_diffuseColorMaxCellParticles / 1024.0f), 1, 1);

            // Add particles to the grid
            kernel = m_colorDiffusionShader.FindKernel("AddParticles");
            m_colorDiffusionShader.SetInt("indexCount", m_totalActiveIndicesCount);
            m_colorDiffusionShader.SetBuffer(kernel, "indices", m_indexBuffer);
            m_colorDiffusionShader.SetBuffer(kernel, "particles", m_particleBuffer);
            m_colorDiffusionShader.SetBuffer(kernel, "grid", m_diffuseColorGridBuffer);
            m_colorDiffusionShader.Dispatch(kernel, Mathf.CeilToInt(m_maxNumActiveIndices / 512.0f), 1, 1);

            // Diffuse colors
            kernel = m_colorDiffusionShader.FindKernel("DiffuseColors");
            m_colorDiffusionShader.SetInt("indexCount", m_totalActiveIndicesCount);
            m_colorDiffusionShader.SetBuffer(kernel, "indices", m_indexBuffer);
            m_colorDiffusionShader.SetBuffer(kernel, "particles", m_particleBuffer);
            m_colorDiffusionShader.SetBuffer(kernel, "colors", oldColorBuffer);
            m_colorDiffusionShader.SetBuffer(kernel, "newColors", newColorBuffer);
            m_colorDiffusionShader.SetBuffer(kernel, "grid", m_diffuseColorGridBuffer);
            m_colorDiffusionShader.Dispatch(kernel, Mathf.CeilToInt(m_maxNumActiveIndices / 512.0f), 1, 1);
         
            // TODO: I don't know if SharedFluidColors should be updated at this time. This whole thing is a mess.
        }

        public override void UpdateColorsBuffer()
        {
            base.UpdateColorsBuffer();
            m_newColorsBuffer.SetData(m_fluidColors.Take(m_numParticles).ToArray());
        }

        protected override void CreateRenderResources()
        {
            base.CreateRenderResources();

            // for fluid mixing
            if (m_colorDiffusionShader)
            {
                m_diffuseColorGridSize.x = Mathf.CeilToInt(m_diffuseColorGridRange.x / m_diffuseColorCellSize);
                m_diffuseColorGridSize.y = Mathf.CeilToInt(m_diffuseColorGridRange.y / m_diffuseColorCellSize);
                m_diffuseColorGridSize.z = Mathf.CeilToInt(m_diffuseColorGridRange.z / m_diffuseColorCellSize);
                Vector3 gridSize;
                gridSize.x = m_diffuseColorGridSize.x;
                gridSize.y = m_diffuseColorGridSize.y;
                gridSize.z = m_diffuseColorGridSize.z;
                CreateComputeBuffer(ref m_newColorsBuffer, sizeof(float) * 4, m_numParticles);
                m_newColorsBuffer.SetData(m_fluidColors.Take(m_numParticles).ToArray()); // this ensures the colors for inactive particles are still corrct and not overwritten
                CreateComputeBuffer(ref m_diffuseColorGridBuffer, sizeof(float) * 4, m_diffuseColorGridSize.x * m_diffuseColorGridSize.y * m_diffuseColorGridSize.z * m_diffuseColorMaxCellParticles);
                m_colorDiffusionShader.SetVector("gridSize", gridSize);
                m_colorDiffusionShader.SetFloat("maxCellParticles", m_diffuseColorMaxCellParticles);
                m_colorDiffusionShader.SetFloat("numParticles", m_numParticles);
                m_colorDiffusionShader.SetFloat("cellSize", m_diffuseColorCellSize);
            }
        }

        protected override void UpdateRenderResources()
        {
            base.UpdateRenderResources();
            DiffuseColor();
        }

        protected override void DestroyRenderResources()
        {
            base.DestroyRenderResources();

            if (m_newColorsBuffer != null) { m_newColorsBuffer.Release(); m_newColorsBuffer = null; }
            if (m_diffuseColorGridBuffer != null) { m_diffuseColorGridBuffer.Release(); m_diffuseColorGridBuffer = null; }
        }

        [SerializeField]
        private Vector3 m_diffuseColorGridRange = new Vector3(10, 10, 10);
        [SerializeField]
        private float m_diffuseColorCellSize = 0.2f;
        [SerializeField]
        private int m_diffuseColorMaxCellParticles = 1;
        [SerializeField]
        private ComputeShader m_colorDiffusionShader;

        private Vector3Int m_diffuseColorGridSize;
        private ComputeBuffer m_newColorsBuffer;
        private ComputeBuffer m_diffuseColorGridBuffer;
        private bool m_swapBuffersFlag = false;
    }
}
