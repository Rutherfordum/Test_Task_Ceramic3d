# Тестовое задание на позицию Unity Developer в компанию Ceramic 3D
Ознакомиться с тестовым заданием можно в папке [StreamingAssets](https://github.com/Rutherfordum/Test_Task_Ceramic3d/tree/main/Assets/StreamingAssets)

## Скриншот
![App Screenshot](https://github.com/Rutherfordum/Test_Task_Ceramic3d/blob/main/Resources/1.png)

## Описание проекта и алгоритма
Основной и единственный код ```C# MatrixOffsetFinderWithJob```, выполняет поиск смещенния матрицы model таким образом что она полностью совподает с матрицей space.

### Загрузка данных json
Данные model.json и space.json храняться в папке StreamingAssets. Для чтения данных используется метод LoadMatricesFromPathAsync, как видим он выполнятеся асинхронно и возвращает список матриц в формате float4x4.

```C#
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
```

### Визуализация данных матриц в Unity
За визуализацию данных отвечает метод VisualizeMatrices.

```C#
  private void VisualizeMatrices(List<float4x4> matrices, Transform prefab)
    {
        matrices.ForEach(m =>
        {
            var ob = Instantiate(prefab, m.c3.xyz, new quaternion(m));
            ob.lossyScale.Set(math.length(m.c0.xyz), math.length(m.c1.xyz), math.length(m.c2.xyz));
        });
    }
```

### Подготовка данных для Burst Compile
Для работы JobSystems нам нужно подготовить данные для паралелизма, поэтому используем NativeArray для передачи данных ModelMatrices, SpaceMatrices, Epsilon и получение данных Offsets.

```C#
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
```

### Поиск смещений
Для начала определяем offset между model и space, после согласно ТЗ выполняем перменожение элементов матрицы model на offset и сравниваем элементы полученной матрицы offsetMatrix c элементами space, причем мне пришлось завести погрешность точности epsilon, т.к. точность float оставялет желать лучшего, а точности double не вижу смысла, т.к. при тесте с значением epsilon = 10^-6 результат был нулевой.

```C#
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
```

### Экспорт смещенний 
Экспорт найденных смещенний в папку StreamingAssets под названием offsetsJob.json.

```C#
 private void ExportOffsetsToJson(List<float4x4> offsets)
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
