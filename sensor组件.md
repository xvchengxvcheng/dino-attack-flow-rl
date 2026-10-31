代码里，这套流程由三个类分工完成：
1. [DinoStructuredObservationBuilder.cs](C:/Users/hp/Documents/New project 2/game_project/dino-attack/Assets/LlamAcademy/Dinos/Training/Runtime/DinoStructuredObservationBuilder.cs)
   从 RoundManager、DinoSpawner、防御布局和部署区对象中读取状态，生成六组数组。同一个 Academy step 内缓存一份结果，六个 Sensor 共用这份快照。
2. [DinoStructuredObservationSensor.cs](C:/Users/hp/Documents/New project 2/game_project/dino-attack/Assets/LlamAcademy/Dinos/Training/Runtime/DinoStructuredObservationSensor.cs)
   实现 ML-Agents 的 ISensor 接口。框架调用它的 Write() 时，它将对应数组写入 ObservationWriter。
3. [DinoStructuredObservationSensorComponent.cs](C:/Users/hp/Documents/New project 2/game_project/dino-attack/Assets/LlamAcademy/Dinos/Training/Runtime/DinoStructuredObservationSensorComponent.cs)
   挂在 Unity 对象上，一次创建六个 Sensor，名称为 00_Global 到 05_Dinos，明确它们的排列顺序。