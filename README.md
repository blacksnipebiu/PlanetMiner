# PlanetMiner

Dyson Sphere Program (戴森球计划) 的 BepInEx 插件，自动在星际运输站矿机上生产矿物。

## 功能

- **自动采矿**：星际运输站矿机位自动从星球矿脉开采矿物
- **能量管理**：自动消耗能量进行采矿，支持能量不足时自动调用燃料产电
- **按工厂缓存**：每个星球工厂独立维护采矿状态，减少重复计算
- **石油产量累计**：石油产量支持小数累计，精确结算整数产出
- **行星半径限制**：可选限制仅在小型星球生效（默认 100 半径）
- **Weaver 兼容**：检测并兼容 Weaver 优化模组的矿脉状态

## 安装

1. 安装 [BepInEx](https://github.com/BepInEx/BepInEx) for Dyson Sphere Program
2. 将 `PlanetMiner.dll` 复制到 `Dyson Sphere Program/BepInEx/plugins/` 目录

## 配置

在游戏设置中配置以下选项：

| 选项 | 默认值 | 说明 |
|------|--------|------|
| `EnablePlanetRadiusLimit` | `true` | 是否启用行星半径限制 |
| `MaxPlanetRadius` | `100` | 生效的最大行星半径（仅当 `EnablePlanetRadiusLimit=true` 时） |
| `EnergyCostMode` | `Fixed` | 矿脉采矿的耗能方式：`Fixed` 或 `MiningMachine`，见下文 |
| `EnergyCostMultiplier` | `1` | `MiningMachine` 模式下，每个矿物的耗能是原版采矿机的多少倍 |
| `MineFuelSlotFirst` | `false` | 先采燃料格（倒数第二格），再采其他栏位。能量不足时每个栏位只采付得起的部分，不开的话微弱的供电会被前面的栏位用光，新建的物流站采不到第一份燃料、无法自己启动 |

修改配置后无需重启游戏。

### 耗能方式

- `Fixed`（默认，与之前的行为相同）：每个栏位每次采矿固定消耗 20 MJ，与采到多少矿物无关。
  平均功率为每栏位 10 MW × 采矿速度倍率，所以矿脉少的星球每个矿物的耗能很高
  （20 个矿脉时每个矿物 1 MJ，约为原版采矿机的 8 倍）。
- `MiningMachine`：按采到的矿物数量计费，每个矿物的耗能 =
  `EnergyCostMultiplier × 120 kJ ÷ 采矿速度倍率`。
  120 kJ 是一台原版采矿机（420 kW，按覆盖 7 个矿脉算）采一个矿物的耗能，
  所以总功率是"用原版采矿机覆盖全星球该矿种"的 `EnergyCostMultiplier` 倍，
  默认设置下为每个矿脉 60 kW。能量不足以采完所有矿脉时，只采付得起的部分。
- 油井类矿脉和水始终按 `Fixed` 计费。

## 系统要求

- Dyson Sphere Program
- BepInEx 5.x 或更高版本
- .NET Framework 4.7.2

## 构建

需要装好游戏。项目会自动从 `E:\Game\Steam\steamapps\common\Dyson Sphere Program` 读取游戏程序集，装在别处时用 `DSPGameRoot` 指过去：

```bash
msbuild PlanetMiner.sln /p:Configuration=Release
# 或：
msbuild PlanetMiner.sln /p:Configuration=Release /p:DSPGameRoot="D:\Steam\steamapps\common\Dyson Sphere Program"
# 输出位于 PlanetMiner/bin/Release/PlanetMiner.dll
```

生成后默认会把 DLL 复制到 `$(DSPGameRoot)\BepInEx\plugins\PlanetMiner\` 并启动游戏；`/p:DeployAfterBuild=false` 跳过复制，`/p:LaunchGameAfterBuild=false` 只复制不启动。

## 发布

`scripts\package.cmd` 打包（走 `-ExecutionPolicy Bypass`，不受系统脚本策略影响）：

```bat
scripts\package.cmd
```

默认会先编译 Release（`dotnet build`，找不到 dotnet 时用 VS 的 MSBuild），再打包；版本号从 `PlanetMiner.cs` 的 `[BepInPlugin]` 读，输出 `artifacts\PlanetMiner-<版本>.zip`。

包内容平铺在压缩包根目录：`PlanetMiner.dll`、`manifest.json`（版本号按参数写入）、`icon.png`、`README.md`。

- 已经有编译结果、不想再编：`scripts\package.cmd -SkipBuild`（Release 不存在时会退回用 Debug 并给出警告）
- 想指定 DLL / 版本 / 输出：`scripts\package.cmd -Dll <路径> -Version 3.1.9 -Out <zip>`
- 需要 Thunderstore 要求的 `BepInEx/plugins/PlanetMiner/PlanetMiner.dll` 目录结构：加 `-GameLayout`

## 工作原理

1. **Tick 节流**：基于 `GameMain.gameTick` 和 `miningSpeedScale` 计算执行间隔
2. **错峰执行**：不同星球使用 `(tick + factoryIndex) % interval` 错开计算时间
3. **矿脉缓存**：每个工厂维护 `VeinsByItem` 字典，按矿物 ID 索引矿脉位置
4. **失效检测**：矿脉耗尽或被移除时标记缓存为失效，触发重建
5. **能量限制**：每个站点每批处理最多触发一次产电，避免重复消耗燃料

## 许可证

MIT License
