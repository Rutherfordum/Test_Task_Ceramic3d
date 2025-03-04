using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[Serializable]
public class OffsetData
{
    public List<float4x4> offsets;
}

public class MatrixOffsetFinderWithJob : MonoBehaviour
{
    private readonly string MODEL_FILE_PATH = Path.Combine(Application.streamingAssetsPath, "model.json");
    private readonly string SPACE_FILE_PATH = Path.Combine(Application.streamingAssetsPath, "space.json");

    [SerializeField] private Transform _modelPrefab;
    [SerializeField] private Transform _spacePrefab;
    [SerializeField] private float _epsilon = 0.001f;

    private List<float4x4> _modelMatricesData;
    private List<float4x4> _spaceMatricesData;

    private NativeArray<float4x4> _modelMatricesNative;
    private NativeArray<float4x4> _spaceMatricesNative;
    private NativeArray<float4x4> _offsetNative;
    private JobHandle _handle;

    public async void Start()
    {
        _modelMatricesData = await LoadMatricesFromPathAsync(MODEL_FILE_PATH);
        _spaceMatricesData = await LoadMatricesFromPathAsync(SPACE_FILE_PATH);

        VisualizeMatrices(_modelMatricesData, _modelPrefab);
        VisualizeMatrices(_spaceMatricesData, _spacePrefab);

        FindOffsetsWithJobs();
    }

    private void VisualizeMatrices(List<float4x4> matrices, Transform prefab)
    {
        matrices.ForEach(m =>
        {
            var ob = Instantiate(prefab, m.c3.xyz, new quaternion(m));
            ob.lossyScale.Set(math.length(m.c0.xyz), math.length(m.c1.xyz), math.length(m.c2.xyz));
        });
    }

    private async Task<List<float4x4>> LoadMatricesFromPathAsync(string path, CancellationToken cancellationToken = default)
    {
        List<float4x4> matrixFloatList = new List<float4x4>();

        string data = await File.ReadAllTextAsync(path, cancellationToken);
        var matrix = JsonConvert.DeserializeObject<List<Matrix4x4>>(data);

        matrix.ForEach(m =>
        {
            matrixFloatList.Add(new float4x4(
                m.GetColumn(0),
                m.GetColumn(1),
                m.GetColumn(2),
                m.GetColumn(3)));
        });

        return matrixFloatList;
    }

    private void FindOffsetsWithJobs()
    {
        int offsetCount = _modelMatricesData.Count * _spaceMatricesData.Count;
        _modelMatricesNative = new NativeArray<float4x4>(_modelMatricesData.ToArray(), Allocator.TempJob);
        _spaceMatricesNative = new NativeArray<float4x4>(_spaceMatricesData.ToArray(), Allocator.TempJob);
        _offsetNative = new NativeArray<float4x4>(offsetCount, Allocator.TempJob);

        var job = new MatrixComparisonJob
        {
            ModelMatrices = _modelMatricesNative,
            SpaceMatrices = _spaceMatricesNative,
            Offsets = _offsetNative,
            Epsilon = _epsilon
        };

        _handle = job.Schedule(_offsetNative.Length, 128);
        _handle.Complete();

        List<float4x4> offsets = new List<float4x4>();

        foreach (var offset in _offsetNative)
        {
            if (!offset.Equals(float4x4.zero))
            {
                offsets.Add(offset);
            }
        }

        ExportOffsetsToJson(offsets);

        _modelMatricesNative.Dispose();
        _spaceMatricesNative.Dispose();
        _offsetNative.Dispose();
    }

    [BurstCompile]
    public struct MatrixComparisonJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float4x4> ModelMatrices;
        [ReadOnly] public NativeArray<float4x4> SpaceMatrices;
        [WriteOnly] public NativeArray<float4x4> Offsets;
        [ReadOnly] public float Epsilon;

        public void Execute(int index)
        {
            int modelIndex = index / SpaceMatrices.Length;
            int spaceIndex = index % SpaceMatrices.Length;

            float4x4 modelMatrix = ModelMatrices[modelIndex];
            float4x4 spaceMatrix = SpaceMatrices[spaceIndex];

            float4x4 offset = CalculateOffset(modelMatrix, spaceMatrix, Epsilon);

            if (!offset.Equals(float4x4.zero))
            {
                Offsets[index] = offset;
            }
        }

        private float4x4 CalculateOffset(float4x4 model, float4x4 space, float epsilon)
        {
            float4x4 offset = space - model;
            float4x4 offsetMatrix = model * float4x4.Translate(offset.c3.xyz);

            if (MatricesAreEqual(offsetMatrix, space, epsilon))
            {
                return offset;
            }

            return float4x4.zero;
        }

        private bool MatricesAreEqual(float4x4 a, float4x4 b, float epsilon)
        {
            float4x4 c = a - b;

            if (CheckFloat4LessEpsilon(c.c0, epsilon) &&
                CheckFloat4LessEpsilon(c.c1, epsilon) &&
                CheckFloat4LessEpsilon(c.c2, epsilon) &&
                CheckFloat4LessEpsilon(c.c3, epsilon))
                return true;

            return false;
        }

        private bool CheckFloat4LessEpsilon(float4 value, float epsilon)
        {
            for (int i = 0; i < 4; i++)
            {
                var val = Mathf.Abs(value[i]);
                if (val > epsilon)
                    return false;
            }

            return true;
        }
    }

    private void ExportOffsetsToJson(List<float4x4> offsets)
    {
        OffsetData data = new OffsetData();
        data.offsets = offsets;
        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(Application.streamingAssetsPath + "/offsetsJob.json", json);
        Debug.Log(Application.streamingAssetsPath + "/offsetsJob.json");
    }
}