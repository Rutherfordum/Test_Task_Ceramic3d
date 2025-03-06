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
    public List<Matrix4x4> offsets;
}

public class MatrixOffsetFinderWithJob : MonoBehaviour
{
    private readonly string MODEL_FILE_PATH = Path.Combine(Application.streamingAssetsPath, "model.json");
    private readonly string SPACE_FILE_PATH = Path.Combine(Application.streamingAssetsPath, "space.json");

    [SerializeField] private Transform _modelPrefab;
    [SerializeField] private Transform _spacePrefab;

    private List<Matrix4x4> _modelMatricesData;
    private List<Matrix4x4> _spaceMatricesData;

    private NativeArray<Matrix4x4> _modelMatricesNative;
    private NativeArray<Matrix4x4> _spaceMatricesNative;
    private NativeArray<Matrix4x4> _offsetNative;
    private JobHandle _handle;

    public async void Start()
    {
        _modelMatricesData = await LoadMatricesFromPathAsync(MODEL_FILE_PATH);
        _spaceMatricesData = await LoadMatricesFromPathAsync(SPACE_FILE_PATH);

        VisualizeMatrices(_modelMatricesData, _modelPrefab);
        VisualizeMatrices(_spaceMatricesData, _spacePrefab);

        FindOffsetsWithJobs();
    }

    private void VisualizeMatrices(List<Matrix4x4> matrices, Transform prefab)
    {
        matrices.ForEach(m =>
        {
            var ob = Instantiate(prefab, m.GetPosition(), new quaternion(m));
            ob.lossyScale.Set(m.lossyScale.x, m.lossyScale.y, m.lossyScale.z);
        });
    }

    private async Task<List<Matrix4x4>> LoadMatricesFromPathAsync(string path, CancellationToken cancellationToken = default)
    {
        List<float4x4> matrixFloatList = new List<float4x4>();

        string data = await File.ReadAllTextAsync(path, cancellationToken);
        var matrixList = JsonConvert.DeserializeObject<List<Matrix4x4>>(data);

        return matrixList;
    }

    private void FindOffsetsWithJobs()
    {
        int offsetCount = _modelMatricesData.Count * _spaceMatricesData.Count;
        _modelMatricesNative = new NativeArray<Matrix4x4>(_modelMatricesData.ToArray(), Allocator.TempJob);
        _spaceMatricesNative = new NativeArray<Matrix4x4>(_spaceMatricesData.ToArray(), Allocator.TempJob);
        _offsetNative = new NativeArray<Matrix4x4>(_spaceMatricesData.ToArray().Length, Allocator.TempJob);

        var job = new FindOffsetMatricesJob(
            _modelMatricesNative,
            _spaceMatricesNative,
            _offsetNative);

        _handle = job.Schedule(_offsetNative.Length, 64);
        _handle.Complete();

        List<Matrix4x4> offsets = new List<Matrix4x4>();

        foreach (var offset in _offsetNative)
        {
            if (!offset.Equals(Matrix4x4.zero))
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
    public struct FindOffsetMatricesJob : IJobParallelFor
    {
        private NativeArray<Matrix4x4> _modelMatrices;
        private NativeArray<Matrix4x4> _spaceMatrices;
        private NativeArray<Matrix4x4> _offsets;
        private Matrix4x4 _modelMatrix;

        public FindOffsetMatricesJob(
            NativeArray<Matrix4x4> modelMatrices,
            NativeArray<Matrix4x4> spaceMatrices,
            NativeArray<Matrix4x4> offsets)
        {
            _modelMatrices = modelMatrices;
            _spaceMatrices = spaceMatrices;
            _offsets = offsets;
            _modelMatrix = modelMatrices[0];
        }
        public void Execute(int index)
        {
            Matrix4x4 offset = _spaceMatrices[index] * _modelMatrix.inverse;

            if (MatricesAreEqual(_modelMatrices, _spaceMatrices, offset))
            {
                _offsets[index] = offset;
            }
        }

        private bool CheckMatrix4x4LessEpsilon(Matrix4x4 matrix, Matrix4x4 comparableMatrix, float floatError = 0.001f)
        {
            for (int i = 0; i < 16; i++)
            {
                if (Mathf.Abs(matrix[i] - comparableMatrix[i]) > floatError)
                {
                    return false;
                }
            }

            return true;
        }

        private bool MatricesAreEqual(
            NativeArray<Matrix4x4> modelMatrices,
            NativeArray<Matrix4x4> spaceMatrices,
            Matrix4x4 offset)
        {
            bool matchFound = false;
            Matrix4x4 transformedMatrix;

            foreach (var modelMatrix in modelMatrices)
            {
                transformedMatrix = math.mul(offset, modelMatrix);

                foreach (var spaceMatrix in spaceMatrices)
                {
                    matchFound = CheckMatrix4x4LessEpsilon(spaceMatrix, transformedMatrix);

                    if (matchFound)
                        break;
                }

                if (!matchFound)
                    return false;
            }

            return true;
        }
    }

    private void ExportOffsetsToJson(List<Matrix4x4> offsets)
    {
        OffsetData data = new OffsetData();
        data.offsets = offsets;
        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(Application.streamingAssetsPath + "/offsetsJob.json", json);
        Debug.Log(Application.streamingAssetsPath + "/offsetsJob.json");
    }


}