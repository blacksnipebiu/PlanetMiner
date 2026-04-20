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

修改配置后无需重启游戏。

## 系统要求

- Dyson Sphere Program
- BepInEx 5.x 或更高版本
- .NET Framework 4.7.2

## 构建

```bash
# 使用 MSBuild
msbuild PlanetMiner.sln /p:Configuration=Release

# 输出位于 PlanetMiner/bin/Release/PlanetMiner.dll
```

## 工作原理

1. **Tick 节流**：基于 `GameMain.gameTick` 和 `miningSpeedScale` 计算执行间隔
2. **错峰执行**：不同星球使用 `(tick + factoryIndex) % interval` 错开计算时间
3. **矿脉缓存**：每个工厂维护 `VeinsByItem` 字典，按矿物 ID 索引矿脉位置
4. **失效检测**：矿脉耗尽或被移除时标记缓存为失效，触发重建
5. **能量限制**：每个站点每批处理最多触发一次产电，避免重复消耗燃料

## 许可证

MIT License
