# 余烬答题显意 · Ember Archive STS2

《杀戮尖塔 2》单人战斗 Mod：每个玩家回合开始时答一道单选题。答对后显示本回合怪物意图；答错、按 Esc 或跳过后，意图继续隐藏，到下一回合再答。每题只有一次作答，不改动伤害、能量或敌人行动。多人战斗不启用意图限制。

## 共享题库和规则

题库只维护在 Mentat-Uran/ember-archive。本仓库不复制题库源码；question-source.lock.json 将其固定到一个完整 Git 提交，构建时稀疏检出题库、题图清单和 packages/question-core/ 到忽略目录 .cache/question-source/，随后仅拉取当前题目引用的题图。

网页和 Mod 的题目构建共用 packages/question-core/index.js 里的复核覆盖、答案索引、作答判定和题目资格规则。Mod 使用 singleAnswerOnly: true，因此多答案题不会出现。更新题库时，改锁文件中的提交 SHA；不要把生成后的题库或题图提交到本仓库。

## 构建

需要 Node.js 20.19+（或 22.12+）、.NET 9 SDK 和已安装的《杀戮尖塔 2》。

    npm run sync:question-source
    npm run mod:pack
    dotnet build EmberIntentQuiz/EmberIntentQuiz.csproj --configuration Release

mod:pack 也会自动同步锁定的共享源。首次同步需要能通过 GitHub 读取题库仓库。脚本优先采用经过像素等价验证和 SHA-256 校验的 WebP 缓存；没有缓存的引用图会从锁定提交检出原始图片。脚本还会校验清单、复核账本摘要、题目数量和资格统计；任一检查不符就停止生成。

完整 Mod 文件夹生成在 output/EmberIntentQuiz/。把整个文件夹复制到游戏安装目录下的 mods/EmberIntentQuiz/，再从游戏内 Mod 列表启用。Steam 不在默认位置时，可设置 STS2_PATH，或在 dotnet build 后添加 /p:Sts2Path="游戏安装目录"。

## 题库和题图权利

题目、材料与图片源自 Mentat-Uran/kaogongzhentizhengliu。源仓库说明，这些内容的版权归原出题机构和题库平台；本项目另行取得的授权范围以权利人许可为准。生成包会附上题库版本和来源提示。未经相应授权，不要再分发生成包或上传 Steam Workshop。

## 技术

Mod 使用游戏自带的 Harmony、Godot C# 与 .NET 9，不依赖 BaseLib。题干、选项、材料和实际引用的题图在本地打包；运行时不连接网络。
