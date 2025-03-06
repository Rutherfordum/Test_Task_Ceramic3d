# Тестовое задание на позицию Unity Developer в компанию Ceramic 3D
Ознакомиться с тестовым заданием можно в папке [StreamingAssets](https://github.com/Rutherfordum/Test_Task_Ceramic3d/tree/main/Assets/StreamingAssets)

## Скриншот
![App Screenshot](https://github.com/Rutherfordum/Test_Task_Ceramic3d/blob/main/Resources/1.png)

## Описание проекта и алгоритма
Основной и единственный код **`MatrixOffsetFinderWithJob`**, выполняет поиск смещенния матрицы model таким образом что она полностью совподает с матрицей space.

### Загрузка данных json
Данные **`model.json`** и **`space.json`** храняться в папке StreamingAssets. Для чтения данных используется метод LoadMatricesFromPathAsync, как видим он выполнятеся асинхронно и возвращает список матриц в формате **`Matrix4x4`**.

```C#
  private async Task<List<Matrix4x4>> LoadMatricesFromPathAsync(string path, CancellationToken cancellationToken = default)
    {
        List<float4x4> matrixFloatList = new List<float4x4>();

        string data = await File.ReadAllTextAsync(path, cancellationToken);
        var matrixList = JsonConvert.DeserializeObject<List<Matrix4x4>>(data);

        return matrixList;
    }
```

### Визуализация данных матриц в Unity
За визуализацию данных отвечает метод **`VisualizeMatrices`**.

```C#
  private void VisualizeMatrices(List<Matrix4x4> matrices, Transform prefab)
    {
        matrices.ForEach(m =>
        {
            var ob = Instantiate(prefab, m.GetPosition(), new quaternion(m));
            ob.lossyScale.Set(m.lossyScale.x, m.lossyScale.y, m.lossyScale.z);
        });
    }
```

### Подготовка данных для Burst Compile
Для работы **`JobSystems`** нам нужно подготовить данные для паралелизма, поэтому используем **`NativeArray<>`** для передачи данных **`ModelMatrices`**, **`SpaceMatrices`** и получение данных **`Offsets`**.

```C#
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
```

### Поиск смещений
Для начала определяем **`offset`** между **`model`** и **`space`**, после согласно ТЗ выполняем перменожение элементов матрицы **`model`** на **`offset`** и сравниваем элементы полученной матрицы **`transformedMatrix`** c элементами **`space`**, причем мне пришлось завести погрешность точности **`0,001f`**.

```C#
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
```

### Экспорт смещенний 
Экспорт найденных смещенний в папку StreamingAssets под названием **`offsetsJob.json`**.

```C#
 private void ExportOffsetsToJson(List<Matrix4x4> offsets)
    {
        OffsetData data = new OffsetData();
        data.offsets = offsets;
        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(Application.streamingAssetsPath + "/offsetsJob.json", json);
        Debug.Log(Application.streamingAssetsPath + "/offsetsJob.json");
    }
```

## Время на решение задачи
- Человеко часы составили 2 дня, с момента открытия ТЗ и редактирования readme git. 
- Сложность алгоритма по времени составила O(M*S), квадратичная сложность.


## Оптимизации
- Фильтрация, если известно, что некоторые матрицы заведомо не могут совпадать, их можно исключить из сравнения, однако в данном случае нам они не известны, поэтому не применяется.
- Паралелизм, используем Job System и Burst Compiler для ускорения выполнения алгоритма, за счет задействования всех ядер процессора.

## Что бы я улучшил?
- Добавил бы UniTask для асинхронной загрузки данных.
- Добавил бы инструмент для отладки Burst Compiler, однако можно запускать в основном потоке Run(int i).
- Сравнение матриц вынес бы в Extension.
- Разделил бы сущности на загрузку и выгрузку данных в виде Json.
- Раздлелил бы сущности на поиск смещенния и визуализацию данных.

## Мои контакты
[Telegram](https://t.me/Rutherfordum)   
[TgChanel](https://t.me/Pro_XR) 
