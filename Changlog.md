# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

### Added

- 新增 BepInEx 配置项 `EnablePlanetRadiusLimit`，用于控制是否启用行星半径限制。
- 新增 BepInEx 配置项 `MaxPlanetRadius`，用于控制 PlanetMiner 生效的最大行星半径。
- 新增按工厂保存的 `FactoryState`，用于缓存矿脉索引、石油小数余量和采矿消耗余量。
- 新增 `OilFractionState`，用于累计石油产量的小数部分。
- 新增 `MyPluginInfo`，并统一插件 GUID、名称与版本号。

### Changed

- 将 `PlanetMiner/PlanetMiner/PlanetMiner.cs` 的实现替换为从 `PlanetMiner-HerSophia/src/PlanetMiner.cs` 同步过来的优化版实现。
- 将节流基准从 `Update()` 中维护的 `frame` 改为 `GameMain.gameTick`。
- 使用 `(tick + factoryIndex) % interval` 让不同行星错峰执行，减少同一时刻的集中计算。
- 将 Weaver 兼容检查中的反射元数据改为首次解析后缓存，避免每次 tick 重复解析。
- 将矿脉扫描改为按工厂缓存，在正常情况下不再每次全量遍历 `veinPool`。
- 将燃料热值查询改为缓存。
- 将石油类型判定改为缓存。
- 将 `GenerateEnergy()` 的调用收敛为每个站点在一次处理流程中最多执行一次。
- 将采矿消耗余量 `costFrac` 从全局静态字段改为按工厂独立保存。
- 将石油产量计算从直接截断改为累计小数余量后再结算整数产出。

### Fixed

- 修正取水分支不检查能量的问题。现在能量不足时不会继续产水。
- 修正 `productRegister[itemId]` 直接写入可能产生越界的问题，增加了边界检查。
- 修正采矿时可能继续使用失效矿脉索引的问题，缓存失效后会触发重建。
- 修正多工厂并行执行时共享 `costFrac` 可能互相影响的问题。
- 修正 Weaver 初始化过程中可能出现的并发读取半初始化状态的问题。

### Notes

- 默认配置下，PlanetMiner 只在 `planet.radius < 100` 的行星上生效。
- 修改配置文件后无需重启游戏即可生效。
- 本次修改主要用于同步 `PlanetMiner-HerSophia/src/PlanetMiner.cs` 中已经整理完成的优化与修正。
