import type { FieldCell, TeamField } from './types';
import { compactSeason, historySeason } from './seasonYears.ts';
import webAvatarIndex from './data/web-avatars.json' with { type: 'json' };

const normalize = (text: string) => text.replace(/[^a-z0-9]/gi, '').toLowerCase();
const terms = new Map<string, string>();

function register(lines: string, target = terms) {
    for (const line of lines.trim().split('\n')) {
        const [source, label] = line.trim().split('=');
        if (source && label) {
            target.set(normalize(source), label);
        }
    }
}

// Display labels never replace keys or authored option values in bridge requests.
register(`
Vitals=基本资料
ID=身份资料
IDs=识别编号
Body=身高与体型
Health=伤病
Team=球队归属
Type=球员类型
Awards=荣誉
Personality=性格
Roster=名单资料
Attributes=能力值
Tendencies=球员倾向
Badges=徽章
Signature=动作
Gear=球鞋与护具
Contract=合同
Stats=统计数据
Advanced=内部参数
Appearance=外貌
Scanned Morph Data=扫描体型
Skin Data=皮肤与纹身
College=学校
Base Tattoo Data=纹身
Player Accessory Data=配饰资料
Player Relationships=关联资料
Offense=进攻
Defense=防守
Athleticism=运动能力
Durability=耐久度
Mental=意识与稳定性
Misc=其他
Progression=成长
Jump Shooting=跳投
Jump Shooting II=其他投篮动作
Layups And Dunks=上篮与扣篮
Drive Setup=突破准备
Driving=突破
Passing=传球
Post Game=背身进攻
Freelance=自由进攻
Hot Zones=投篮热区
Ball Handling=控球
Contract Terms=合同条款
Contract Options=合同选项
Contract Status=合同状态
Salary=薪资
Inside Scoring=内线得分
Outside Scoring=外线得分
Playmaking=组织进攻
Defending=防守
Rebounding=篮板
General=通用
Gameplay=比赛表现
Flags=状态标记
Shoes/Gear=球鞋与服装
Upper Body Accessories=上身护具
Lower Body Accessories=下身护具
Career High Stats=生涯单场最高
Season High Stats=赛季单场最高
Awards & Honors=奖项与荣誉
Season Context=赛季资料
Season IDs=赛季编号
Stat IDs=统计编号
Scoring & Impact=得分与影响
Ratings=评分
Participation=出场
Milestones=里程碑
History=历史
Edit=统计编辑
Engine=引擎数据
Other=其他
Reserved=保留数据
Identity=身份
Internal=内部数据
Links=关联数据
Dates=日期
Color=颜色
Info=球队资料
Jerseys=球衣资源
Business=经营
Budget=预算
Pricing=票价与定价
Team Players=球员名单槽位
Two Way Player 1=双向合同球员一
Two Way Player 2=双向合同球员二
Two Way Player 3=双向合同球员三
Team Lineups=战术阵容配置
Team Advanced=球队内部参数
Team City=球队城市
Team Stadium=球馆
Team Stats=球队统计
Team Stats Edit=球队统计编辑
Team History=球队历史
Team Season Stats=赛季统计
Team Latest Record=球队纪录
Team Dead Cap=死钱合同
Team Trade Exceptions=交易特例
Team Retired Jerseys=退役球衣
Teams=球队
Climate & Tax=气候与税率
Geography=地理信息
Market=市场
Relocation=搬迁
Visuals=视觉资源
Classification=分类
Fields=资料
Bench=替补
Custom=自定义
Free Throw=罚球
Quick=快速
Starters=首发
Tall=高个阵容
3PT=三分阵容
Contract/Salary=合同与薪资
Uniform Details - Practice=训练服
Body Profile=体型
Body Hair=体毛
Brows=眉骨
Cheeks=脸颊
Chin=下巴
Details=细节
Ears=耳朵
Eyes=眼睛
Hair=头发与胡须
Head=头部
Lips=嘴唇
Mouth=嘴部
Nose=鼻子
Presentation=外观
Skin=皮肤
Teeth=牙齿
Placement=位置
Style=样式
Status=状态
Brand=品牌
Scan Data=扫描数据
Scan Files=扫描文件
Scan Settings=扫描设置
Tattoo Files=纹身文件
Blacklight Tattoo Files=荧光纹身文件
Other Assets=其他资源
Attribute Boosts=能力加成
Reputation Boosts=声望加成
Eligibility & Flags=资格与状态
Prospect Generation=新秀生成
Record=战绩
Schedule & Conference=赛程与分区
Audio=音频
First Name=名字
Last Name=姓氏
Age=年龄
Birth Day=出生日期
Birth Month=出生月份
BirthYear=出生年份
BirthPlace=出生地
College/From=学校或来源
FROM=来源
Height=身高（厘米）
Weight=体重（千克）
WingspanInTenthsOfMM=臂展（厘米）
Wingspan (cm)=臂展（厘米）
Wingspan=臂展
BodyShape=体型
Dominant Hand=惯用手
Dominant Dunk Hand=扣篮惯用手
Jersey Number=球衣号码
Jersey Nick Name=球衣昵称
Nick Name=昵称
Created Player Nickname=自建球员昵称
Position=主要位置
Secondary Position=次要位置
Third Position=第三位置
Years Pro=职业生涯年数
YearsWithTeam=效力当前球队年数
Peak Start Age=巅峰开始年龄
Peak End Age=巅峰结束年龄
PeakAge=巅峰年龄
PeakStart=巅峰开始年龄
PeakEnd=巅峰结束年龄
Potential=潜力
OriginalPotential=原始潜力
Maximum Potential=最高潜力
Minimum Potential=最低潜力
Boom Percentage=成才概率
Bust Percentage=衰退概率
Average Percent=正常发展概率
BoringPercent=平稳发展概率
Boom Or Bust=成长风险类型
Draft Pick Number=选秀顺位
Round Drafted=选秀轮次
Drafted Year=被选中年份
Drafted Team=选秀球队
DraftProspectIndex=待选秀顺序
DraftYear=选秀年份
DraftRound=选秀轮次
DraftPick=选秀顺位
Play For Winner=争冠意愿
Financial Security=经济保障意愿
Loyalty=忠诚度
Play Initiator=发起进攻
Play Type 1=战术类型一
Play Type 2=战术类型二
Play Type 3=战术类型三
Play Type 4=战术类型四
PlayingTime=出场时间
Force Non Starter=固定替补
Custom Age at Set Year=指定年份年龄
Name Order=姓名顺序
FlipFirstLastNames=交换姓名顺序
JerseyNameType=球衣姓名格式
HighSchool=高中
Retirement=退役状态
Is Active=在役
IsActive_Private=当前在役
Is Draft Prospect=待选秀球员
Is Historic=历史球员
Is Generated=生成球员
Is Hidden=隐藏球员
IsPlayable=可上场
Player Type=球员风格
OffensivePlayerType=进攻风格
League=所属联盟
OrgRole=球队角色
SeasonRetired=退役赛季
IsGLeague=发展联盟球员
MustRetireNextYear=下赛季退役
Was Drafted=已经被选中
IsCelebrity=名人球员
IsGenerationalPlayer=天才新秀
IsEligibleForFantasyDraft=可参加梦幻选秀
IsProtectedInDraft=选秀保护
IsDraftedInTeamExpansion=扩军选秀已被选中
TwoWayWasCalledUpToday=双向球员今日已召回
RosterType=名单类型
RosterYear=名单年份
Current Team=当前球队
Previous Team=之前球队
Current Team Address=当前球队关联
Portrait Team 1=肖像球队一
Portrait Team 2=肖像球队二
Hometown Team 1=家乡球队一
Hometown Team 2=家乡球队二
TeamHistory0TeamId=球队经历一
TeamHistory1TeamId=球队经历二
TeamHistory2TeamId=球队经历三
TeamHistory0Date=球队经历一日期
TeamHistory1Date=球队经历二日期
TeamHistory2Date=球队经历三日期
3pt Shot=三分投篮
Ball Control=控球
Close Shot=近距离投篮
Draw Foul=造犯规
Driving Dunk=突破扣篮
Driving Layup=突破上篮
Free Throws=罚球
Hands=接球
Midrange Shot=中距离投篮
Offensive Rebound=进攻篮板
Pass Accuracy=传球准确性
Passing IQ=传球意识
Passing Vision=传球视野
Post Fade=背身后仰
Post Hook=背身勾手
Post Moves=背身控制
Shot IQ=投篮选择
Standing Dunk=原地扣篮
Block=盖帽
Defensive Rebound=防守篮板
Interior Defense=内线防守
Perimeter Defense=外线防守
Steal=抢断
Acceleration=加速
Agility=敏捷
Hustle=积极性
Speed=速度
Speed with Ball=持球速度
Stamina=体力
Strength=力量
Vertical=弹跳
Defensive Consistency=防守稳定性
Help Defense IQ=协防意识
Offensive Consistency=进攻稳定性
Pass Perception=传球预判
Intangibles=无形资产
Lateral Quickness=横向移动
Pick And Roll Defensive IQ=挡拆防守意识
ReactionTime=反应时间
Shot Contest=干扰投篮
Max Ovr=最高总评
Min Ovr=最低总评
Cachced Ovr=缓存总评
DeclineCurve=衰退曲线
GrowthCurve=成长曲线
ProgressionPlayerType=成长类型
Miscanellous Durability=其他部位耐久
Contested Jumper 3pt=受干扰三分
Contested Jumper Mid=受干扰中投
Contested Jumper Mid-Range=受干扰中投
Drive Pull Up 3pt=突破急停三分
Drive Pull Up Mid=突破急停中投
Drive Pull Up Mid-Range=突破急停中投
Off Screen Shot 3pt=绕掩护三分
Off Screen Shot Mid=绕掩护中投
Shot 3pt=三分出手
Shot Close=近距离出手
Shot Mid=中距离出手
Shot Under Basket=篮下出手
Spin Jumper Tendency=转身跳投
Spot Up Shot 3pt=定点三分
Spot Up Shot Mid=定点中投
Step Through Shot=跨步投篮
Stepback Jumper 3pt=后撤步三分
Stepback Jumper Mid=后撤步中投
Stepback Jumper Mid-Range=后撤步中投
Transition Pull Up 3pt=转换急停三分
Use Glass=打板投篮
Alley-Oop=空接终结
Crash=冲抢进攻篮板
Driving Dunk Tendency=突破扣篮
Driving Layup Tendency=突破上篮
Euro Step Layup=欧洲步上篮
Flashy Dunk=花式扣篮
Floater=抛投
Hop Step Layup=跳步上篮
Putback=补篮
Putback Dunk=补扣
Spin Layup=转身上篮
Standing Dunk Tendency=原地扣篮
No Setup Dribble=不做准备运球
Setup With Hesitation=犹豫步准备
Setup With Sizeup=花式运球准备
Triple Threat Idle=三威胁观察
Triple Threat Jab Step=三威胁试探步
Triple Threat Pump Fake=三威胁投篮假动作
Triple Threat Shoot=三威胁投篮
Attack Strong On Drive=强力突破
Drive=突破
Drive Right=向右突破
Driving Behind The Back=突破背后运球
Driving Crossover=突破变向
Driving Double Crossover=突破连续变向
Driving Dribble Hesitation=突破犹豫步
Driving Half Spin=突破半转身
Driving In And Out=突破内外运球
Driving Spin=突破转身
Driving Step Back=突破后撤步
No Driving Dribble Move=突破不做运球动作
Off Screen Drive=绕掩护突破
Spot Up Drive=定点接球突破
Alley Oop Pass=空接传球
Dish To Open Man=传给空位队友
Flashy Pass=花式传球
Post Aggressive Backdown=强力背打
Post Back Down=背身靠打
Post Drive=背身突破
Post Drop Step=背身沉底步
Post Face Up=背身转面框
Post Fade Left=向左背身后仰
Post Fade Right=向右背身后仰
Post Hook Left=向左勾手
Post Hook Right=向右勾手
Post Hop Shot Tendency=背身跳步投篮
Post Hop Step=背身跳步
Post Shimmy Shot=背身晃肩投篮
Post Spin=背身转身
Post Step Back Shot=背身后撤步投篮
Post Up=背身进攻
Post Up And Under=背身上下步
PostDropStepInsideTendency=向内沉底步
PostDropStepOutsideTendency=向外沉底步
Shoot From Post=背身出手
Iso Vs Average Defender=单打普通防守者
Iso Vs Elite Defender=单打顶尖防守者
Iso Vs Good Defender=单打优秀防守者
Iso Vs Poor Defender=单打较弱防守者
Play Discipline=战术执行
Roll Vs Pop=顺下与外弹选择
RollVsFadeTendency=顺下与外撤选择
Shoot=出手
Touches=触球
Transition Spot Up=转换定点接球
Block Shot=尝试盖帽
Contest Shot=干扰投篮
Foul=犯规
Hard Foul=强硬犯规
On Ball Steal=持球抢断
Pass Interception=拦截传球
Take Charge=制造进攻犯规
Animation Blending=动作混合比例
Jumpshot Blending=投篮混合比例
Jumpball Ritual=跳球准备动作
Jumpshot Base=投篮基础动作
ShotJumper=投篮基础动作
Pre-Game 1=赛前动作一
Pre-Game 2=赛前动作二
Release Timing=出手速度
ShotTimingImpactModifier=出手时机影响
Upper Release 1=上肢出手动作一
Upper Release 2=上肢出手动作二
Dribble Pull-Up=运球急停跳投
Go-To Shot=招牌投篮
ShotSideHop=横跳投篮
ShotSpin=转身投篮
ShotStepThru=跨步投篮
Dunk Emotion=扣篮情绪
Go-To Dunk Package=招牌扣篮包
Layup Package=上篮包
Post Go-To Shot=背身招牌投篮
Post Hop Shot=背身跳步投篮
Post Spin Shot=背身转身投篮
Post Spin Spot=背身转身位置
PostOneStepPullup=背身单步急停
Aggressive Breakdown Combos=强力运球组合
Breakdown Combos=运球组合
Crossover Combos=变向组合
Dribble Style=运球风格
Escape Moves=摆脱动作
IsoBehindBack=原地背后运球
IsoBehindBackLaunch=背后运球启动
IsoBreakdown=原地运球组合
IsoBreakdownMove=原地运球衔接
IsoComboDoublecross=双变向组合
IsoComboHesicross=犹豫步变向组合
IsoCrossCombo=交叉运球组合
IsoCrossover=原地变向
IsoCrossSpin=变向接转身
IsoEscapeBehindback=背后运球摆脱
IsoEscapeCross=变向摆脱
IsoEscapeHes=犹豫步摆脱
IsoEscapeTween=胯下摆脱
IsoHesitation=原地犹豫步
IsoHesitationLateral=横向犹豫步
IsoInandOut=内外运球
IsoMisdirectBehindback=反向背后运球
IsoMisdirectCross=反向变向
IsoMisdirectHes=反向犹豫步
IsoSizeupSig=招牌花式运球
IsoSpin=原地转身
IsoStepback=原地后撤步
IsoStepbackCross=后撤步变向
IsoStepbackLateral=横向后撤步
IsoTweenlegs=原地胯下运球
Moving Behind The Back=行进间背后运球
Moving Crossover=行进间变向
Moving Hesitation=行进间犹豫步
Moving Spin=行进间转身
Moving Stepback=行进间后撤步
Pass Style=传球风格
Regular Breakdown Combos=普通运球组合
Signature Size-Up=招牌花式运球
Big Size-Up=大个花式运球
Size-Up Escape Packages=花式摆脱包
Spin Jumper=转身跳投
Triple Threat Style=三威胁风格
TripleThreatBreakdown=三威胁组合
TripleThreatJab=三威胁试探步
TripleThreatStepover=三威胁跨步
Chew Gum=嚼口香糖
GumChewer=嚼口香糖
Hop Jumper=跳步投篮
Lower/Base=下肢基础动作
Motion Style=移动风格
NBA Jumpball Ritual=跳球准备动作
SigJumpballStand=跳球站姿
SigPlayerIntro1=入场动作一
SigPlayerIntro2=入场动作二
FreefallStyle=倒地动作
EmoteDRB=运球庆祝动作
EmoteTT=三威胁庆祝动作
VoiceType=声音类型
Bird Years=鸟权年数
Contract Length=合同年限
Contract Team=签约球队
Contract Thought=合同态度
Extension Length=续约年限
FA Status=自由球员资格
Free Agency Type=自由球员类型
Years Left=剩余年限
Years Left On Contract=合同剩余年限
Original Contract Years=原始合同年限
Contract Type=合同类型
Option=球队或球员选项
No Trade=不可交易条款
Extension No Trade=续约不可交易条款
Extension Option=续约选项
ContractOption=合同选项
HasQualifyingOffer=已收到资质报价
IsWaived=已被裁掉
OffSeasonFreeAgent=休赛期自由球员
WasBoughtOut=已买断
Type=类型
Two-Way NBA Days Left=双向合同剩余天数
Two-Way Days Left=双向合同剩余天数
LastYearWage=上赛季薪资
Aerial Wizard=空中魔术师
Float Game=抛投高手
GhostStepperBadge=幻影脚步
Hook Specialist=勾手专家
Layup Mixmaster=上篮大师
Paint Prodigy=禁区神童
Physical Finisher=强力终结者
Post Powerhouse=背身强攻
Post-Up Poet=背身艺术家
Posterizer=隔人扣篮
PostSpinCatalystBadge=背身转身大师
Rise Up=原地起飞
ArcCadenceBadge=弧线节奏
Deadeye=神射手
Limitless Range=无限射程
Mini Marksman=小个射手
Post Fade Phenom=背身后仰大师
QuickTriggerBadge=快速出手
SetAndFireBadge=定点开火
Shifty Shooter=灵活射手
Slippery Off-Ball=无球跑位
SmoothOperatorBadge=流畅终结
StaticMiddyBadge=定点中投
Ankle Assassin=脚踝终结者
AnkleAssassinBadge=脚踝终结者
Bail Out=应急传球
Break Starter=快攻发起者
BreakerBadge=突破大师
Dimer=十美分
Handles for Days=持球耐力
Lightning Launch=闪电启动
Strong Handle=强力控球
Unpluckable=护球大师
Versatile Visionary=全能传球手
AnkleBracesBadge=脚踝护卫
Challenger=投篮干扰者
Glove=手套
High-Flying Denier=高空封锁
Interceptor=拦截者
Off-Ball Pest=无球缠绕
On-Ball Menace=持球防守威胁
Paint Patroller=禁区巡逻者
Pick Dodger=绕掩护高手
Pogo Stick=连续起跳
Post Lockdown=背身防守大师
SeatbeltBadge=贴身锁防
SyncSnatcherBadge=节奏抢断
WallUpBadge=筑墙防守
Brick Wall=铜墙铁壁
Immovable Enforcer=坚不可摧
WorkHorseBadge=拼抢达人
Boxout Beast=卡位大师
BoxoutBossBadge=卡位领袖
CrasherBadge=冲板高手
Rebound Chaser=篮板追逐者
Alpha Dog=领袖
BigEgoBadge=自我中心
SmallEgoBadge=谦逊
BigMarketPlayerBadge=大市场偏好
FairWeatherPlayerBadge=顺境型球员
TaxAvoiderPlayerBadge=低税率偏好
HighFriendlinessBadge=友善
LowFriendlinessBadge=不易亲近
NegativelyMotivatedBadge=逆境激励
PositivelyMotivatedBadge=正向激励
Enforcer=强硬领袖
Expressive=情绪外露
Extremely Confident=极度自信
Finance Savvy=精于理财
Friendly=友善
Keep It Real=直率
Laid Back=随和
Marketability=商业吸引力
Media Ringmaster=媒体焦点
Pat My Back=需要鼓励
Reserved=内敛
Team Player=团队球员
Unpredictable=难以预测
Warm Weather Fan=暖地偏好
Work Ethic=职业态度
BruiserBadge=身体对抗
FlashBadge=闪击
PaceBadge=节奏掌控
PossessionCloserBadge=回合终结
NONEBadge=无性格徽章
Arm Scale=手臂比例
HandScale=手掌大小
LowerScale=下身比例
Neck Length=颈部长度
NeckHeadScale=头颈比例
Shoulder Length=肩部长度
ShoulderScale=肩部比例
ShoulderWidth=肩宽
Trunk Length=躯干长度
TrunkScale=躯干比例
BodyLength=身体长度
SkinType=皮肤体态
FaceType=脸型
Gender=性别
Gender Created Player=自建球员性别
UseCustomScales=自定义身体比例
Face ID=面容样式
Headshot ID=头像样式
Portrait ID=肖像样式
Audio Signature ID=解说姓名
Signature ID=动作识别编号
Unique Signature ID=唯一识别编号
ChinaFirstName=中文名字编号
ChinaLastName=中文姓氏编号
FirstNameVetted=名字已审核
LastNameVetted=姓氏已审核
LocalizedLastName=本地化姓氏
LocalizedCityName=城市名文本引用
LocalizedShortTeamName=球队简称文本引用
LocalizedStateName=州名文本引用
UnlocalizedCityName=城市名（原文）
UnlocalizedShortTeamName=球队简称（原文）
UnlocalizedStateName=州名（原文）
UnlocalizedFirstName=原始名字
UnlocalizedLastName=原始姓氏
Overall=综合评分
Assists=助攻
Blocks=盖帽
Defensive Rebounds=防守篮板
Offensive Rebounds=进攻篮板
Field Goals Attempted=投篮出手
Field Goals Made=投篮命中
Free Throws Attempted=罚球出手
Free Throws Made=罚球命中
Minutes Played=出场时间
Minutes=出场时间
Points=得分
Rebounds=篮板
Steals=抢断
Three Pointers Attempted=三分出手
Three Pointers Made=三分命中
Games Played=出场次数
Games Started=首发次数
Double Doubles=两双
Triple Doubles=三双
Turnovers=失误
Fouls=犯规
Total +/-=正负值
Most Valuable Player=最有价值球员
Defensive Player Of The Year=最佳防守球员
Finals MVP=总决赛最有价值球员
Most Improved Player=进步最快球员
Rookie Of The Year=最佳新秀
Sixth Man=最佳第六人
All-NBA Team=最佳阵容
All-Defense Team=最佳防守阵容
All-Rookie Team=最佳新秀阵容
All Star West=西部全明星
All Star East=东部全明星
AllStarStarter=全明星首发
AllStarReserve=全明星替补
ClutchPlayer=关键球员奖
ISTChampion=季中锦标赛冠军
Championship=总冠军
City Name=城市名称
Team Name=球队名称
State=球队经营方向
State Short Name=州简称
Short Team Name=球队简称
Historic Year=历史赛季
Division=赛区
DefaultEra=默认时代
Fan Interest=球迷兴趣
Team Chemistry=球队默契
Player Payroll=球员薪资总额
Staff Payroll=工作人员薪资总额
Salary=薪资
Arena Name=球馆名称
Arena Nickname=球馆昵称
Stadium=球馆
Coach=教练
Wins=胜场
Losses=负场
Ranking=排名
Current Year Stat ID=本赛季统计编号
CityId=城市编号
RegionId=地区编号
BaseTeamId=基础球队编号
CommonId=通用球队编号
IsUserControlled=玩家控制球队
BenchStand=替补席站立
HeadCoachJobSecurity=主教练职位稳定度
WinterTemp=冬季温度
StateTax=州税率
Latitude=纬度
Longitude=经度
TVHomes=电视覆盖家庭
Population=人口
AreaGDP=地区生产总值
GrowthPhase=城市发展阶段
CanRelocateTo=允许搬迁
TeamStrength=球队实力
GenerationChance=生成概率
GenerationPotential=生成潜力
NumberOfProspects=新秀人数
JerseyBagginess=球衣宽松度
HairStyle=发型
HairColor=发色
HairColor2=第二发色
HairLength=头发长度
EyeColor=瞳色
SkinColor=肤色
BeardType=胡须样式
GoateeType=山羊胡样式
MoustacheType=八字胡样式
SideburnType=鬓角样式
IsScannedHead=扫描头部标记
IsModified=外貌已修改
CapType=自建头部类型
HybridId=混合面容编号
CrowdExcitementMultiplier=观众兴奋程度
DaysInGLeague=发展联盟效力天数
DraftedFantasyPick=梦幻选秀顺位
DraftedFantasyRound=梦幻选秀轮次
EurostashRedraft=海外球员重新选秀
EvolutionLevel=进化等级
Is G-League Send Down=下放发展联盟
IsFourOut=四外战术球员
IsSocialMediaFollower=社交媒体关注状态
IsSummerLeagueAttendee=参加夏季联赛
MinEvoLevelForSecondaryPosition=解锁次要位置所需等级
NickName_Low=昵称备用编号
PositionEdited=位置已编辑
ProgressionTotalTeamRegulationGameLength=成长计算用球队总比赛时长
ProYearCounted=职业年数已计入
RecentDraftSigning=近期签约新秀
RetiredJerseyNumber=球衣号码已退役
ShuffledDraftYear=重排选秀年份
Special Case Name=特殊姓名格式
UnfazedHiddenTrait=沉着隐藏性格
Used Retirement Grace=已使用延迟退役机会
HadCareerEndedByInjury=因伤结束生涯
FIBATeamId=国际篮联球队编号
DeadCapTemporaryPlayerType=死钱合同临时球员类型
WNBAArchetype=女子联盟球员模板
HallOfFameRating=名人堂评分
MVPAwardShares=最有价值球员奖项得票份额
PlayerOfMonth=月最佳球员
PlayerOfWeek=周最佳球员
RookieOfMonth=月最佳新秀
RookieOfWeek=周最佳新秀
Shoe Away Colorway=客场球鞋配色
Shoe Home Colorway=主场球鞋配色
Shoe Locked Vendor (Broken)=球鞋品牌锁定（旧版项）
Shot 3pt Center=弧顶三分出手
Shot 3pt Left=左侧三分出手
Shot 3pt Left Center=左侧45度三分出手
Shot 3pt Right=右侧三分出手
Shot 3pt Right Center=右侧45度三分出手
AmbientPackage1=闲置动作包一
AmbientPackage1Weight=闲置动作包一权重
AmbientPackage2=闲置动作包二
AmbientPackage2Weight=闲置动作包二权重
AmbientPackage3=闲置动作包三
AmbientPackage3Weight=闲置动作包三权重
NumContractIncentives=合同激励条款数量
NoTradeDaysLeft=禁止交易剩余天数
OffSeasonFreeAgentExtendedDeadline=休赛期自由球员延长截止日期
SecondTenDayContractWithTeam=本队第二份十天合同
Ankle Breaker=脚踝终结者
Set Shot Specialist=定点投篮专家
ABB=球队简称
AudioChantVariation=观众助威口号
BaseAttendance=基础上座人数
BaselineFanDemand=基础球迷需求
BaseYearOffsetWhenAddedViaExpansion=扩军加入时年份差
Chemistry=球队默契
City Abbrev=城市简称
CityTaxBreak=城市税收优惠
Classic Team Year=经典球队年份
Concession Prices=餐饮售价
ConferenceRankingWithTiesIsTied=分部排名并列
ConsecutiveYearsWithTop5Pick=连续获得前五顺位年数
Crowd Id=观众样式
DeadCapData=死钱合同资料
DivisionRankingWithTiesIsTied=赛区排名并列
FileName=资源文件名
FreeAgentOffersRemaining=剩余自由球员报价次数
G-League Team Address=发展联盟关联球队
HadFirstOverallPickLastYear=上赛季获得状元签
IsAddedViaExpansion=通过扩军加入
Is Downtown=位于市中心
LeagueRankingWithTiesIsTied=联盟排名并列
LevelMaterialVariationName=场景材质版本
LocalizedSlogan=本地化球队口号
Luxury Box Prices=豪华包厢票价
Luxury Tax=奢侈税
Mantra=球队口号
ManualBuildLineup3Pter=手动设置三分阵容
ManualBuildLineupFreeThrow=手动设置罚球阵容
Metro Income M=都市收入（百万）
MLE Remaining=剩余中产特例
MovieFileName=视频文件名
Non Bird Exception Available=非鸟权特例可用
NumberOfAcquiredPlayers=本年新援人数
NumberOfAcquiredPlayersLastYear=去年新援人数
NumberOfAcquiredPlayersMoreYearsAgo=更早年份新援人数
NumberOfCorePlayers=核心球员人数
NumberOfEuroStashPlayers=海外签约权球员人数
NumberOfHardshipPlayers=困难特例球员人数
NumberOfLineupPlayers=阵容球员人数
NumberOfTwoWayPlayers=双向合同球员人数
PlayoffClinchRank=锁定季后赛席位排名
Popularity=人气
Practice Jersey Away=客场训练服
Practice Jersey Home=主场训练服
Promotions=促销
Records=纪录
Rival 1 Team=宿敌球队一
Rival 2 Team=宿敌球队二
Rival 3 Team=宿敌球队三
Rival1=宿敌球队一
Rival2=宿敌球队二
Rival3=宿敌球队三
Show City In Name=名称显示城市
SloganFileName=口号资源文件
SoldoutGamesThisSeason=本赛季售罄场次
Text Dark=深色文字颜色
Text Light=浅色文字颜色
Tickets=门票
TradeCashInRemaining=交易可收现金额度
TradeCashOutRemaining=交易可付现金额度
UnlocalizedSlogan=原始球队口号
User Edited=玩家已编辑
IsExtraRosterToolTeam=额外名单球队
Mascot Size=吉祥物大小
MascotTail=吉祥物尾巴
MLE Type=中产特例类型
PlayoffClinchType=季后赛席位类型
StandingsClinchType=锁定排名类型
2nd Logo=第二标志
3D Logo=立体标志
Arena File Name=球馆资源文件
ArenaScene=球馆场景
BaseLogoName=基础标志名称
EmojiLogo=表情标志
LED Id=球馆灯带样式
LogoFlat3DName=平面立体标志
LogoMural=壁画标志
LogoScene=场景标志
LogoWordmarkName=文字标志
LogoWordmarkNameAlt=备用文字标志
MainLogo=主要标志
Mural Logo=壁画标志
Neno Logo A=霓虹标志一
Neno Logo B=霓虹标志二
NeonLogoAName=霓虹标志一名称
NeonLogoBName=霓虹标志二名称
Pa Sound ld=球馆广播音效
Pa Sound Id=球馆广播音效
Play By Play Sound ld=实况解说音效
Play By Play Sound Id=实况解说音效
SecondaryLogo=次要标志
WM Logo=文字标志
WM Logo Alt=备用文字标志
CustomLogoColorBlue=自定义标志蓝色分量
CustomLogoColorGreen=自定义标志绿色分量
CustomLogoColorRed=自定义标志红色分量
IsSwapPrimarySecondaryColor=交换主色与辅色
Shot Meter Color=投篮条颜色
LossesAhead3Q=三节领先但输球场次
LossesBehind3Q=三节落后且输球场次
LossesFewerTOs=失误较少但输球场次
LossesLeadingFGPct=命中率领先但输球场次
LossesLeadingRebs=篮板领先但输球场次
WinsAhead3Q=三节领先且赢球场次
WinsBehind3Q=三节落后但赢球场次
WinsFewerTOs=失误较少且赢球场次
WinsLeadingFGPct=命中率领先且赢球场次
WinsLeadingRebs=篮板领先且赢球场次
Teamcolor1=球队颜色一
Teamcolor2=球队颜色二
Teamcolor3=球队颜色三
MyTeamAuctionable=梦幻球队可拍卖
MyTeamColor=梦幻球队颜色
MyTeamDuplicateId=梦幻球队重复编号
MyTeamInclude=梦幻球队收录状态
MyTeamMomentDate=梦幻球队时刻日期
MyTeamOfflineOnly=梦幻球队仅离线
MyTeamRevealUniformId=梦幻球队展示球衣编号
MyTeamStadiumRevealAnim=梦幻球队球馆展示动画
MyTeamTheme=梦幻球队主题
MyTeam OverrideLogoName=梦幻球队自定义标志
MyTeamContractsRemaining=梦幻球队剩余合同
CachedDefenseRating NeverCall=防守缓存评分
CachedOffenseRating NeverCall=进攻缓存评分
CachedOverallRating NeverCall=综合缓存评分
Possessions=回合数
ConferenceOvrRatingRanking=分部总评排名
ConferenceOvrRatingRankingByPosition=分部同位置总评排名
ConferencePERScoreRanking=分部效率值排名
ConferencePERScoreRankingByPosition=分部同位置效率值排名
LeagueOvrRatingRanking=联盟总评排名
LeagueOvrRatingRankingByPosition=联盟同位置总评排名
LeaguePERScoreRanking=联盟效率值排名
LeaguePERScoreRankingByPosition=联盟同位置效率值排名
TeamFinishedAt Internal=结束时所属球队
TeamStartedAt Internal=开始时所属球队
FiftyPointGames=单场50分次数
FortyPointGames=单场40分次数
SixtyPointGames=单场60分次数
PointsOffFreeThrows=罚球得分
CompactYear=赛季
AreCachedRatingsValid=评分缓存有效
IsGameDataHack=内部数据修改标记
PreservedId=保留编号
HasDoneDunkContest=已参加扣篮大赛
HasFakeAttributes=使用替代能力值
HasUsedTradeReasonThisSeasonInternal=本赛季已使用交易理由
ActionShotId=动态照片编号
ClanId=群组编号
FirstLastNameSID=姓名文本编号
FranchisePlayerDataIndex=球队球员资料序号
GraphicId=图像资源编号
MuralId=壁画编号
OffspringParentUniqueId=后代球员父辈编号
PhotoId=照片编号
ShotChartDataIndex=投篮分布图序号
PNOTier=快速在线比赛档位
AllStar3Pter=参加全明星三分大赛
AllStarDunker=参加全明星扣篮大赛
DateLastChitChatUsed=上次闲聊日期
DaysSinceDeclarationToExtend=表示愿意续约后的天数
FaceOfTheFranchise=球队门面
MakeACoach=退役后成为教练
NoMadDunk=特殊扣篮标记（含义待核实）
ProScoutingIndex=职业球探资料序号
After Badge Padding=徽章后的保留数据
After Season Stats Padding=赛季统计后的保留数据
MyPlayerClothesType=自建角色服装类型
NextBaseTattooData=下一条纹身资料
Location=纹身位置
AdamSilverSoundId=选秀唱名音频编号
ClosestNBATeamId=最近的联盟球队编号
Attribute1Boost=第一项能力加成
Attribute1Type=第一项能力加成类型
Attribute1Type=第一项加成能力
ActionShotTeam Pointer=动态照片球队关联
BaseTattooData Pointer=纹身资料关联
BlacktopData Pointer=街球资料关联
HometownTeam Pointer=家乡球队关联
PlayerStatDataListOverride Pointer=自定义球员统计关联
PortraitTeam Pointer=肖像球队关联
ChestTattooBlackLightFilename=胸部荧光纹身文件
FaceTattooBlackLightFilename=面部荧光纹身文件
LegsTattooBlackLightFilename=腿部荧光纹身文件
TorsoTattooBlackLightFilename=躯干荧光纹身文件
NeighborhoodFilename=街区外观文件
FaceScanInputJsonFilename=面部扫描配置文件
LastAttemptedFaceScanInputJsonFilename=最近尝试的面部扫描配置文件
LegsTattooFilename=腿部纹身文件
BonusBuzz=额外关注度
BudgetTier=预算档位
IsSecondApronTeam=达到第二土豪线
PremiumExpense=高级设施支出
Unallocated Funds=未分配资金
Concessions=餐饮收入
Ticket=门票收入
IsManifestCustomLogoDirty=自定义标志待更新
MuralPlayer Internal=球队壁画球员关联
TeamStatDataListOverride=自定义球队统计关联
ISTGroupNumber=季中锦标赛分组
ContainsMultiplePlayersWithTheSameUniqueId=含有重复编号球员
ISTIndexInGroup=季中锦标赛组内序号
HistoricSeasons=历史赛季数量
LastWinningSeasonYear=最近胜率过半的赛季
RetiredJerseyData=退役球衣资料
YearsRemainToHostAllStar=距离主办全明星年数
StarsDowntownArenaUpgradeCost=市中心球馆升级费用
StarsDowntownAttendance=市中心球馆上座人数
StarsDowntownOperationCost=市中心球馆运营费用
StarsSuburbsArenaUpgradeCost=郊区球馆升级费用
StarsSuburbsAttendance=郊区球馆上座人数
StarsSuburbsOperationCost=郊区球馆运营费用
RuralImage=郊区图像
UrbanImage=城市图像
NextDeadCapData=下一条死钱合同
Stat=统计项目
IsEmpty=空记录
RetiredYear=退役年份
NextRetiredJerseyData=下一条退役球衣
CrowdWearWeight=观众穿着权重
NumTenures=效力时期数量
OpponentThreePointShotsAttempted=对手三分出手
OpponentThreePointShotsMade=对手三分命中
TurnoversForced=制造失误
FastBreakPoints=快攻得分
PointsInPaint=内线得分
PointsOffTurnovers=利用失误得分
SecondChancePoints=二次进攻得分
ThreePointShotsAttempted=三分出手
ThreePointShotsMade=三分命中
Stadium Dornas=场边广告牌样式
Stadium Floor ID=球场地板样式
Stadium LED ID=球馆灯带样式
2ndpts=二次进攻得分
L=负场
O3PA=对手三分出手
O3PM=对手三分命中
OFGA=对手投篮出手
OFGM=对手投篮命中
PA=失分
PIP=内线得分
Poss=回合数
Pts To=利用失误得分
Turnover=失误
W=胜场
ExpirationDate=到期日期
PendingRenounce=等待放弃权利
`);

const words: Record<string, string> = {
    left: '左', right: '右', center: '中路', middle: '中部', upper: '上', lower: '下',
    front: '前', rear: '后', back: '背部', head: '头部', neck: '颈部', shoulder: '肩部',
    arm: '手臂', elbow: '肘部', wrist: '手腕', finger: '手指', fingers: '手指',
    hand: '手部', hip: '髋部', hips: '髋部', leg: '腿部', knee: '膝盖', ankle: '脚踝',
    foot: '脚部', toe: '脚趾', thigh: '大腿', calf: '小腿', chest: '胸部', torso: '躯干',
    trunk: '躯干', body: '身体', forearm: '前臂', upperarm: '上臂', durability: '耐久',
    shot: '投篮', hot: '热', zone: '区', close: '近距离', mid: '中距离', med: '中距离',
    three: '三分', '3pt': '三分', under: '下', basket: '篮筐', tendency: '倾向',
    dunk: '扣篮', package: '动作包', salary: '薪资', wage: '薪资', year: '年', years: '年',
    contract: '合同', extension: '续约', incentive: '激励', promise: '承诺',
    importance: '重要性', option: '选项', type: '类型', player: '球员', team: '球队',
    home: '主场', away: '客场', color: '颜色', item: '样式', items: '样式',
    frequency: '佩戴频率', paired: '同步', is: '是否', use: '使用', uses: '使用',
    custom: '自定义', preferred: '偏好', shorts: '短裤', compression: '紧身',
    sock: '球袜', socks: '球袜', length: '长度', shoe: '球鞋', shoes: '球鞋',
    locked: '锁定', vendor: '品牌', style: '风格', undershirt: '内衬',
    shirts: '上衣', shirt: '上衣', warmup: '热身', outfit: '服装', configuration: '配置',
    bench: '替补', ingame: '比赛中', pregame: '赛前', hoodie: '连帽衫',
    accessories: '护具', headband: '头带', logo: '标志', position: '位置',
    face: '面部', mask: '面罩', goggles: '护目镜', armtape: '手臂贴布',
    mouthpiece: '牙套', ability: '能力', injury: '伤病', duration: '持续天数',
    status: '状态', healed: '康复', percentage: '百分比', original: '原始',
    start: '开始', end: '结束', date: '日期', latest: '最近', slot: '位置',
    days: '天数', since: '距今', last: '最近', aggravation: '加重',
    disable: '禁用', career: '职业生涯', ending: '结束', allowed: '允许',
    disallowed: '不允许', flags: '标记', important: '重大', recovery: '恢复',
    had: '曾有', num: '数量', number: '数量', games: '比赛', missed: '缺席',
    due: '因', to: '', pre: '之前', accessory: '护具', part: '部位',
    total: '总计', points: '得分', assists: '助攻', blocks: '盖帽', steals: '抢断',
    rebounds: '篮板', turnovers: '失误', offensive: '进攻', defensive: '防守',
    field: '运动战', goals: '投篮', attempted: '出手', made: '命中',
    free: '自由', throws: '罚球', threepoint: '三分', shots: '投篮',
    percent: '百分比', rating: '评分', ratings: '评分', overall: '综合',
    conference: '分部', league: '联盟', ranking: '排名', by: '按',
    season: '赛季', history: '历史', score: '得分', value: '数值', count: '数量',
    minutes: '分钟', played: '出场', game: '比赛', current: '当前',
    previous: '之前', acquired: '获得', remaining: '剩余', lefttime: '剩余',
    roster: '名单', playoff: '季后赛', playoffs: '季后赛', arena: '球馆',
    operations: '运营', staff: '工作人员', budget: '预算', payroll: '薪资总额',
    premium: '高级', attendance: '上座人数', cumulative: '累计',
    initial: '初始', lastyear: '去年', wins: '胜场', losses: '负场',
    streak: '连胜负', longest: '最长', win: '胜', loss: '负', ahead: '领先',
    behind: '落后', half: '半场', fewer: '较少', leading: '领先',
    division: '赛区', ties: '并列', with: '', without: '无',
    manual: '手动', build: '建立', lineup: '阵容', enable: '启用',
    starter: '首发', starters: '首发', quick: '快速', tall: '高个',
    twoway: '双向合同', euro: '欧洲', stash: '海外签约权',
    hardship: '困难特例', core: '核心', imported: '导入', expansion: '扩军',
    offers: '报价', trade: '交易', cash: '现金', exception: '特例',
    name: '名称', city: '城市', state: '州', localized: '本地化',
    unlocalized: '原始', short: '短', unique: '唯一', signature: '识别',
    id: '编号', crc: '校验编号', index: '序号', data: '数据', internal: '内部',
    pointer: '关联', next: '下一个', first: '首个', padding: '保留项',
    unused: '未使用', temporary: '临时', flag: '标记', reference: '引用',
    cached: '缓存', valid: '有效', description: '描述', preserve: '保留',
    should: '应当', bitfield: '位标记', system: '体系', mastery: '熟练度',
    wear: '磨损', and: '与', tear: '消耗', drafted: '被选中', draft: '选秀',
    rookie: '新秀', scouting: '球探', progressive: '累积', fatigue: '疲劳',
    fitness: '状态', jersey: '球衣', sales: '销量', recently: '近期',
    traded: '已交易', not: '不', interested: '感兴趣', animation: '动作',
    takeover: '主宰', special: '特殊', abilities: '能力', modifier: '修正',
    immunity: '抗干扰', load: '加载', management: '管理', managment: '管理',
    regime: '方案', fake: '替代', attributes: '能力', pending: '待完成',
    done: '已参加', contest: '大赛', big: '大', missing: '缺失',
    art: '美术资源', created: '自建', if: '当', as: '作为',
    hair: '毛发', facial: '面部', fin: '发梢', underpaint: '底色',
    eyebrow: '眉毛', eyebrows: '眉毛', eye: '眼睛', eyes: '眼睛',
    eyelash: '睫毛', eyelids: '眼睑', lip: '嘴唇', lips: '嘴唇',
    nose: '鼻子', chin: '下巴', cheek: '脸颊', ear: '耳朵', ears: '耳朵',
    teeth: '牙齿', tooth: '牙齿', skull: '颅骨', temple: '太阳穴',
    brow: '眉骨', mouth: '嘴部', skin: '皮肤', muscle: '肌肉',
    width: '宽度', height: '高度', thickness: '厚度', prominence: '突出程度',
    preset: '预设', rotation: '旋转', angle: '角度', scale: '比例',
    distance: '间距', size: '大小', dimple: '凹陷', gap: '间隙', dent: '凹陷',
    crease: '褶皱', cutout: '缺口', circles: '眼圈', pattern: '图案',
    fade: '渐变', corner: '边缘', colorization: '染色', beard: '胡须',
    goatee: '山羊胡', moustache: '八字胡', sideburn: '鬓角', chesthair: '胸毛',
    cap: '头部', expression: '表情', rig: '骨骼', modified: '已修改',
    scanned: '扫描', morph: '形变', morphs: '形变', normal: '正常',
    thick: '粗', thin: '细', abs: '腹部', glutes: '臀部', breast: '胸部',
    trap: '斜方肌', spine: '脊柱', upleg: '大腿', toebase: '脚趾根部',
    tx: '横向位移', ty: '纵向位移', tz: '深度位移',
    r: '红', g: '绿', b: '蓝', x: '横向', y: '纵向', z: '深度',
    xpos: '横向位置', ypos: '纵向位置', xscale: '横向比例', yscale: '纵向比例',
    filename: '文件名', blacklight: '荧光', tattoo: '纹身',
    facepaint: '面部彩绘', paint: '彩绘', scars: '伤疤', scarring: '疤痕',
    freckles: '雀斑', moles: '痣', blemishes: '斑点', discolorations: '色斑',
    wrinkles: '皱纹', pock: '痘', marks: '印', alt: '替代',
    grill: '牙饰', anim: '动画', input: '输入', capture: '采集', lores: '低精度',
    json: '配置', noise: '噪点', uv: '贴图', pore: '毛孔', tiling: '平铺',
    roughness: '粗糙度', max: '最大', min: '最小', pit: '腋下', iris: '虹膜',
    white: '白色', lash: '睫毛', only: '仅', debug: '调试', gen: '世代',
    college: '学校', region: '地区', metro: '都市', area: '区域',
    eligibility: '资格', high: '高', school: '学校', generation: '生成',
    chance: '概率', potential: '潜力', prospects: '新秀', conf: '分区',
    non: '非', closest: '最近', china: '中国', exclude: '排除',
    sound: '音效', group: '组', relationship: '关系', instance: '实例',
    brand: '品牌', guid: '标识', string: '文本', boost: '加成', rep: '声望',
    multiplier: '倍率', used: '使用', user: '玩家', controlled: '控制',
    championships: '总冠军数', titles: '冠军数', appearances: '参赛次数',
    historic: '历史', lottery: '乐透', picks: '选秀签', opponent: '对手',
    round: '轮次', series: '系列赛', strength: '强度', schedule: '赛程',
    cashin: '现金收入', cashout: '现金支出', freeagent: '自由球员',
    acquiredplayers: '新援', training: '训练', camps: '营', of: '', from: '来源',
    ten: '十', day: '天', cooldown: '冷却', signing: '签约', bought: '买',
    out: '断', waived: '被裁', resign: '续签', offseason: '休赛期',
};

export function translated(text: string): string | null {
    if (!text || /[\u3400-\u9fff]/.test(text)) {
        return text;
    }
    const direct = terms.get(normalize(text)) ?? terms.get(normalize(text.replace(/^PLAYERACCESSORYDATA::/, '')));
    if (direct) {
        return direct;
    }
    const boostable = text.match(/^Is(.+?)(?:Badge)?Boostable$/i);
    if (boostable) {
        const badge = terms.get(normalize(boostable[1])) ?? terms.get(normalize(`${boostable[1]}Badge`));
        return badge ? `${badge}可提升` : null;
    }
    const clean = text.replace(/^[*]+/, '').replace(/^PLAYERACCESSORYDATA::/, '')
        .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2')
        .replace(/([a-z])([A-Z])/g, '$1 $2');
    const tokens = clean.match(/\d+pt|[a-zA-Z]+|[0-9]+|[一-龥]+/gi) ?? [];
    const pieces: string[] = [];
    for (const token of tokens) {
        const value = terms.get(normalize(token)) ?? words[token.toLowerCase()]
            ?? ({ uniform: '球衣', directives: '指令', up: '上', toe: '脚趾', base: '根部',
                way: '向', throw: '罚球', free: '自由' } as Record<string, string>)[token.toLowerCase()];
        if (value !== undefined) {
            pieces.push(value);
        } else if (/^\d+$/.test(token)) {
            pieces.push(token);
        } else {
            return null;
        }
    }
    return pieces.length > 0 ? pieces.join('') : null;
}

export const sectionLabel = (text: string) => text === 'Personality' ? '场上习惯' : translated(text) ?? '其他资料';
type NamedField = Pick<FieldCell, 'label' | 'key'>;

const lineupNames: Record<string, string> = {
    '3PT': '三分阵容', Bench: '替补阵容', Custom: '自定义阵容', Defense: '防守阵容',
    'Free Throw': '罚球阵容', Quick: '速度阵容', Starters: '首发阵容', Tall: '高个阵容',
};

// Context is part of the meaning: a college rating and a city identifier are not NBA team fields.
function contextualFieldLabel(field: NamedField): string | undefined {
    const [section, group, name] = field.key.split('/');
    if (section === 'College') {
        if (name === 'REGIONID') return '学校所属地区';
        if (name === 'TEAMSTRENGTH') return '学校球队实力参数';
    }
    if (section === 'Team City' && name === 'REGIONID') return '城市所属地区编号';
    const history = name?.match(/^TEAMHISTORY([0-2])(DATE|TEAMID)$/);
    if (history) return `效力记录 ${Number(history[1]) + 1} · ${history[2] === 'DATE' ? '日期编码' : '球队编号'}`;
    if (section === 'Team Players') {
        const slot = name?.match(/^(PLAYER|2WAYPLAYER)(\d+)$/);
        if (slot) return `${slot[1] === 'PLAYER' ? '常规名单' : '双向合同'}第 ${slot[2]} 位`;
    }
    if (section === 'Team Lineups') {
        const slot = name?.match(/PLAYER(\d+)$/);
        if (slot && lineupNames[group]) return `${lineupNames[group]}第 ${slot[1]} 位`;
    }
    if (section === 'Jerseys') {
        const jersey = name?.match(/^JERSEY(\d+)$/);
        if (jersey) return `球衣资源 ${jersey[1]}`;
        const uniform = name?.match(/^UNIFORM(\d+)$/);
        if (uniform) return `球衣资源 ${Number(uniform[1]) + 1} · 内部关联`;
    }
    return undefined;
}

export const fieldLabel = (field: NamedField) =>
    contextualFieldLabel(field) ?? translated(field.label)
    ?? translated(field.key.split('/').at(-1) ?? '') ?? '内部参数（含义待核实）';

export function fieldGroupLabel(field: Pick<FieldCell, 'key' | 'group' | 'section'>): string {
    if (field.group === 'Personality') {
        return field.section === 'Badges' ? '性格徽章' : '场上习惯';
    }
    if (field.section === 'Team Lineups') return lineupNames[field.group] ?? sectionLabel(field.group);
    return sectionLabel(field.group);
}

export function fieldDescription(field: NamedField): string | undefined {
    const [section, , name] = field.key.split('/');
    if (/^TEAMHISTORY[0-2]DATE$/.test(name ?? '')) {
        return '效力记录保存的原始日期编码；当前资料未定义编码规则，不能按普通年月日填写。记录顺序也未确认。';
    }
    if (/^TEAMHISTORY[0-2]TEAMID$/.test(name ?? '')) {
        return '该条效力记录关联的球队内部编号；记录顺序未确认，与当前所属球队分开保存。';
    }
    if (/^LOCALIZED(?:CITY|SHORTTEAM|STATE)NAME$/.test(name ?? '')) {
        return '游戏多语言名称的内部 64 位引用，当前显示原始数值；原文名称另行保存。州名引用与球队经营方向无关。';
    }
    if (name === 'STATE') return '球队经营策略：维持现状、补强、出售球员或重建。';
    if (name === 'EMOTEDRB') return '运球状态使用的庆祝或挑衅动作；与运球风格、变向动作分别设置。';
    if (name === 'EMOTETT') return '三威胁持球状态使用的庆祝或挑衅动作；与试探步、投篮假动作倾向分别设置。';
    if (name === 'GUMCHEWER' || name === 'CHEWGUM') return '是否显示嚼口香糖的场上习惯；性格徽章在“徽章”的“性格徽章”分组。';
    if (section === 'College') {
        if (name === 'REGIONID') return '该球员关联学校的地区分类；HISPANIC 为原始分类名，不能据此推定具体国家。';
        if (name === 'TEAMSTRENGTH') return '学校资料中新秀生成分组的球队实力参数；资料未提供评分单位、范围或对生成结果的换算规则。';
        return '来自该球员关联的学校资料；同一学校可能被多名球员引用。';
    }
    if (section === 'Team City' && name === 'REGIONID') return '球队城市资料中的地区内部编号；当前资料未提供编号与地区名称的对照。';
    if (section === 'Jerseys') {
        return /^UNIFORM\d+$/.test(name ?? '')
            ? '球队球衣槽位保存的内部关联；对应球衣资源名称单独显示。'
            : '该槽位关联的球衣资源名称；主客场名称与前两个球衣资源槽位存在别名。';
    }
    if (section === 'Team Lineups') return '该套战术阵容的球员关联，五个槽位分别保存；不会单独代表整支球队的名单。';
    if (section === 'Team Players') return '球队常规或双向合同名单中的球员关联；完整球员名单与调队操作可在球队阵容页查看。';
    return undefined;
}

export function fieldSectionDescription(section: string): string | undefined {
    return ({
        Jerseys: '球队各球衣槽位关联的资源名称与内部关联。',
        'Team Lineups': '按首发、替补、三分、防守等用途保存的五人阵容配置。',
        'Team Players': '球队常规与双向合同名单槽位保存的球员关联。',
        College: '球员关联的学校资料，包含地区、校队记录及新秀生成参数。',
    } as Record<string, string>)[section];
}

const primarySections = new Set([
    'Vitals', 'Body', 'Health', 'Attributes', 'Tendencies', 'Badges', 'Signature', 'Gear', 'Contract',
    'ID', 'Team', 'Type', 'Awards', 'Personality', 'Roster', 'Stats', 'Appearance', 'College',
]);
export const isRoutineField = (field: FieldCell | TeamField) =>
    primarySections.has(field.section)
    && translated(field.label) !== null
    && !/(INTERNAL|NEVERCALL|PADDING|MYTEAM|BOOSTABLE)/i.test(field.key);

// These authored team fields store links, packed state or bytes rather than readable values.
const teamLinks = /^(?:CITY|STADIUM|DEADCAPDATA|TRADEEXCEPTIONDATA|RECORDS|EMOJILOGO|GLEAGUETEAM(?:ADDRESS)?|PRACTICEJERSEY(?:AWAY|HOME)|RIVAL[123](?:TEAM)?|LASTYEARPLAYOFFSOPPONENT|LOCALIZED(?:CITYNAME|SHORTTEAMNAME|STATENAME|TEAMNAME|SLOGAN)|COREPLAYER\d+|EUROSTASHPLAYER\d+|HARDSHIPPLAYER\d+|LINEUPPLAYER\d+|UNIFORM\d+)$/;
const teamPacked = /^(?:ADVANCEDROTATION|LAST10|LAST10AWAY|LAST10HOME|CURRENTINDEX|LEVELMATERIALVARIATIONNAME)$/;
// The schema does not define the year base, season text encoding or signed streak conversion.
const teamRawCodes = /^(?:CLASSICTEAMYEAR|HISTORICYEAR|HISTORICSEASONS|(?:AWAY|HOME|PREVIOUS)?WINLOSSSTREAK)$/;

export function isRoutineTeamField(field: FieldCell | TeamField): boolean {
    const [section, group, name = ''] = field.key.split('/');
    if (section === 'Team Players' || section === 'Team Lineups') return true;
    return section !== 'Team Advanced' && !/^(?:Internal|Links|Engine)$/.test(group)
        && !/Internal|Pointer|Address|Padding|NextFree|CRC|UNUSED/i.test(field.key)
        && !teamLinks.test(name) && !teamPacked.test(name) && !teamRawCodes.test(name)
        && !(section === 'Vitals' && group === 'Color')
        && !/^TEXT(?:DARK|LIGHT)$/.test(name)
        && field.display !== 'System.Byte[]' && field.value !== 'System.Byte[]'
        && isConfirmedField(field);
}

export function isConfirmedField(field: FieldCell | TeamField): boolean {
    const [section, , name = field.key] = field.key.split('/');
    if (section === 'Team Players' || section === 'Team Lineups') return true;
    if (!field.key.includes('/') && field.kind === 'choice'
        && /^(?:TEAM|OPPONENT|LASTYEARPLAYOFFSOPPONENT|TEAMINDEX|CHAMPIONTEAM)$/.test(name)
        && field.value !== null && isConfirmedOption(String(field.value), field)) return true;
    if (/^TEAMHISTORY[0-2]DATE$/.test(name)
        || name === 'NOMADDUNK'
        || section === 'Signature' && /^AMBIENTPACKAGE[1-3](?:WEIGHT)?$/.test(name)
        || section === 'College' && name === 'TEAMSTRENGTH'
        || section === 'Team City' && name === 'REGIONID'
        || teamLinks.test(name) || teamPacked.test(name)
        || /Internal|Pointer|Address|Padding|NextFree|CRC|UNUSED/i.test(field.key)
        || field.display === 'System.Byte[]' || field.value === 'System.Byte[]'
        || fieldLabel(field).startsWith('内部参数')) return false;
    return field.kind !== 'choice' || field.value !== null
        && isConfirmedOption(String(field.value), field);
}

export function teamLabel(name: string): string {
    if (!name) {
        return '归属待确认';
    }
    return name;
}

const choices = new Map<string, string>();
register(`
None=无
Normal=普通
Late Boom=后期爆发
Linear=线性变化
Logarithmic=先快后慢
Exponential=先慢后快
TRUE_WITH_RESET=启用，可重置
TRUE_FOREVER=始终启用
A B=名在前，姓在后
B A=姓在前，名在后
LOW_POST=低位背打
HIGH_POST=高位背打
GUARD_POST=后卫背打
HANDOFF=手递手接球
HANDOFF_BALLHANDLER=手递手持球
SCREEN_MID=绕掩护中投
SCREEN_3PT=绕掩护三分
FIRST_TWO=名字前两个部分
ACCESSORY_BODY_PART_NONE=无护具部位
PLAYERDATA_TEAM_HOME=主队
PLAYERDATA_TEAM_AWAY=客队
PLAYERDATA_TEAM_NONE=无球队
FULL=完整
LIMITED=有限
NBA=NBA 联盟
WNBA=WNBA 联盟
SEGA=SEGA 分区
Small=小
Non Taxpayer=未缴奢侈税中产特例
Taxpayer=缴纳奢侈税中产特例
Cap Room=薪资空间中产特例
Online=在线球队
ALLSTARS_EAST=东部全明星
ALLSTARS_WEST=西部全明星
All Stars East=东部全明星
All Stars West=西部全明星
Street Home=街球主队
Street Away=街球客队
Rookies=一年级新秀队
Sophomores=二年级新秀队
Rookie Showcase=新秀展示队
MYTEAM_TEAM=梦幻球队
MyTeam=梦幻球队
Euroleague=欧洲联赛
Domination=称霸赛
COLLEGE_MENS=大学男篮
COLLEGE_WOMENS=大学女篮
AAU=业余联盟
Ignite=点燃队
SPORTS_2K=2K 体育队
2K Sports=2K 体育队
Hoops Summit=篮球峰会队
FIBA=国际篮联球队
FIBA_HIDDEN=国际篮联隐藏球队
Expansion Premade=预设扩军球队
Historic Snapshot=历史阵容快照
All Time=历代最佳阵容
E League=电竞联赛
Decade=年代球队
REBRAND_TEMPLATE=球队改名模板
Hidden=隐藏
Randomized=随机生成
Coed=男女混合
Playin=附加赛
Unknown=未知
Team USA=美国队
Dream Team=梦之队
Mamba Moments=曼巴时刻
BEST_RECORD=最佳战绩
TAX_TEAM=缴纳奢侈税球队
Unlimited=无限制
Declining=衰退期
Booming=快速增长期
PTS=得分
FGM=投篮命中
TPM=三分命中
FTM=罚球命中
REB=篮板
AST=助攻
BLK=盖帽
STL=抢断
TOS=失误
PPG=场均得分
FGP=投篮命中率
TPP=三分命中率
FTP=罚球命中率
RPG=场均篮板
APG=场均助攻
BPG=场均盖帽
SPG=场均抢断
MPG=场均上场时间
GP=出场次数
FLS=犯规
PTS40=单场四十分次数
PTS50=单场五十分次数
PTS60=单场六十分次数
TD=三双次数
PLAYER_SINGLE_SEASON_TEAM_RECORD=球员单赛季队史纪录
PLAYER_SINGLE_SEASON_LEAGUE_RECORD=球员单赛季联盟纪录
PLAYER_ROOKIE_SINGLE_SEASON_LEAGUE_RECORD=新秀单赛季联盟纪录
TWO_WAY_PLAYMAKER=攻防组织者
SLASHING_PLAYMAKER=突破组织者
INSIDE_OUT_PLAYMAKER=内外线组织者
TWO_WAY_SLASHER=攻防突破手
THREE_AND_D=三分与防守
THREE_LEVEL_SCORER=三层次得分手
OFFENSIVE_THREAT=进攻威胁
SHARPSHOOTER=神射手
PLAYMAKING_SHOT_CREATOR=组织与自主得分
POST_SCORER=背身得分手
LOCKDOWN_DEFENDER=防守尖兵
PAINT_BEAST=禁区猛将
MVP=最有价值球员
DPOY=最佳防守球员
ALLNBA1=最佳阵容一队
ALLNBA2=最佳阵容二队
ALLNBA3=最佳阵容三队
ALLDEFENSE=最佳防守阵容
CONFERENCE_FINALS=分区决赛
NBA_FINALS=总决赛
SIGN_BUDDY=签下好友
KEEP_A_FRIEND=留住好友
GET_RID_OF_ENEMY=送走不合的球员
NEVER_SIGN_ENEMY=不签下不合的球员
KEEP_PLAYER_UNTIL_RETIREMENT=留队直至退役
2 Yr Team=两年球队选项
Rookie Restricted=新秀受限自由球员
WILL_RESIGN=愿意续约
WILL_RETIRE=准备退役
TEST_FA=试水自由球员市场
REFUSE_RESIGN=拒绝续约
Noitem=无
Unequipped=未装备
No=否
Yes=是
Off=关闭
On=开启
False=否
True=是
Null=无
N/A=无
Bronze=铜
Silver=银
Gold=金
Hall Of Fame=名人堂
Legend=传奇
Legendary=传奇
Normal=标准
Default=默认
Base=基础
Basic=基础
Pro=专业
Fundamental=基础
Default Small=默认后卫
Default Swing=默认锋线
Default Big=默认中锋
Very Slow=非常慢
Slow=慢
Quick=快
Very Quick=非常快
Strong=强
Very Strong=非常强
PG=控球后卫
SG=得分后卫
SF=小前锋
PF=大前锋
C=中锋
Left=左手
Right=右手
Either=双手
Playerdata Besthand Left=左手
Playerdata Besthand Right=右手
Male=男
Female=女
Female Nonplayer=女性非球员
Child=儿童
Teen=青少年
Average=标准
Ripped=健壮
Doughy=圆润
IsOlation=单打
Iso Point=控卫单打
Iso Wing=侧翼单打
PNR Ballhandler=挡拆持球人
PNR Point=控卫挡拆
PNR Wing=侧翼挡拆
PNR Screener=挡拆掩护人
Post Low=低位背打
Post High=高位背打
Cutter=空切
Mid Range=中距离
3pt=三分
Guard Post Up=后卫背打
Handoff Receiver=手递手接球
Handoff Passer=手递手传球
All Around=全能
Two Way=攻防兼备
Offensive=进攻型
Defensive=防守型
Athletic=运动型
Rebounding=篮板型
Playmaking=组织型
Outside=外线型
Inside=内线型
Expendable=可交易
Discard=可放弃
Capspace=腾出薪资空间
Prospect=培养对象
Tenured=资深球员
Core=核心球员
Foundation=建队基石
Hardship=困难特例
Euro Stash=海外签约权
Lineup=阵容球员
Healthy=健康
Probable=大概率出战
Questionable=出战成疑
Doubtful=大概率缺阵
Out=缺阵
No Injury=无伤病
APPENDECTOMY=阑尾切除手术
ARTHROSCOPIC_SURGERY=关节镜手术
BACK_SPASMS=背部痉挛
BLOODY_NOSE=鼻出血
BONE_SPURS=骨刺
BROKEN_ANKLE=脚踝骨折
BROKEN_ARM=手臂骨折
BROKEN_ELBOW=肘部骨折
BROKEN_FINGER=手指骨折
BROKEN_FOOT=足部骨折
BROKEN_FOREARM=前臂骨折
BROKEN_HAND=手部骨折
BROKEN_JAW=下颌骨折
BROKEN_KNEECAP=髌骨骨折
BROKEN_LEG=腿部骨折
BROKEN_NOSE=鼻骨骨折
BROKEN_RIBS=肋骨骨折
BROKEN_SHOULDER=肩部骨折
BROKEN_THIGH=大腿骨折
BROKEN_TOE=脚趾骨折
BROKEN_VERTEBRAE=椎骨骨折
BROKEN_WRIST=手腕骨折
BRUISED_BICEP=肱二头肌挫伤
BRUISED_FOREARM=前臂挫伤
BRUISED_HEEL=脚跟挫伤
BRUISED_HIP=髋部挫伤
BRUISED_KNEE=膝部挫伤
BRUISED_LOWER_BACK=下背部挫伤
BRUISED_RIB=肋部挫伤
BRUISED_SHOULDER=肩部挫伤
BRUISED_STERNUM=胸骨挫伤
BRUISED_TAILBONE=尾骨挫伤
COLLAPSED_LUNG=气胸
CONCUSSION=脑震荡
CRACKED_NECK_VERTEBRAE=颈椎骨裂
DEEP_THIGH_BRUISE=大腿深度挫伤
DEHYDRATION=脱水
DISLOCATED_ELBOW=肘部脱臼
DISLOCATED_FINGER=手指脱臼
DISLOCATED_PATELLA=髌骨脱位
ELBOW_SURGERY=肘部手术
ELBOW_TENDINITIS=肘部肌腱炎
EYE_SURGERY=眼部手术
FATIGUE=疲劳
FLU=流感
FOOD_POISONING=食物中毒
FOOT_STRESS_FRACTURE=足部应力性骨折
FOOT_SURGERY=足部手术
FRACTURED_ARM=手臂骨裂
FRACTURED_EYE_SOCKET=眼眶骨折
FRACTURED_LEG=腿部骨裂
HAIRLINE_ARM_FRACTURE=手臂细微骨裂
HAIRLINE_FINGER_FRACTURE=手指细微骨裂
HAIRLINE_HAND_FRACTURE=手部细微骨裂
HAND_SURGERY=手部手术
HEAD_LACERATION=头部割伤
HERNIA=疝气
HIGH_ANKLE_SPRAIN=高位踝扭伤
HIP_SURGERY=髋部手术
HYPEREXTENDED_ELBOW=肘部过度伸展
HYPEREXTENDED_KNEE=膝部过度伸展
HYPEREXTENDED_SHOULDER=肩部过度伸展
INNER_EAR_INFECTION=内耳感染
KNEE_SURGERY=膝部手术
KNEE_TENDINITIS=膝部肌腱炎
LEG_CRAMP=腿部抽筋
LOWER_BACK_STRAIN=下背部拉伤
LOWER_LEG_STRESS_FRACTURE=小腿应力性骨折
MICROFRACTURE_SURGERY=微骨折手术
MIGRAINE=偏头痛
NECK_SPASMS=颈部痉挛
NOSE_LACERATION=鼻部割伤
PERSONAL_REASON=个人原因
PINCHED_BACK_NERVE=背部神经受压
PINCHED_ELBOW_NERVE=肘部神经受压
PINCHED_NECK_NERVE=颈部神经受压
PINCHED_SHOULDER_NERVE=肩部神经受压
PLANTAR_FASCIITIS=足底筋膜炎
POKE_IN_EYE=眼部被戳伤
PULLED_BICEP=肱二头肌拉伤
RUPTURED_ACHILLES=跟腱断裂
SELF_ISOLATING=自我隔离
SEPARATED_SHOULDER=肩锁关节脱位
SEVERE_ANKLE_SPRAIN=严重脚踝扭伤
SHIN_SPLINTS=胫骨疼痛
SHOULDER_TENDINITIS=肩部肌腱炎
SPRAINED_SHOULDER=肩部扭伤
SORE_ANKLE=脚踝酸痛
SORE_BACK=背部酸痛
SORE_FOOT=足部酸痛
SORE_HAMSTRING=腿后肌群酸痛
SORE_HAND=手部酸痛
SORE_KNEE=膝部酸痛
SORE_NECK=颈部酸痛
SORE_WRIST=手腕酸痛
SPINAL_INJURY=脊柱受伤
SPRAINED_ANKLE=脚踝扭伤
SPRAINED_ELBOW=肘部扭伤
SPRAINED_FINGER=手指扭伤
SPRAINED_FOOT=足部扭伤
SPRAINED_KNEE=膝部扭伤
SPRAINED_TOE=脚趾扭伤
SPRAINED_WRIST=手腕扭伤
STOMACH_VIRUS=肠胃病毒感染
STRAINED_ABDOMEN=腹部拉伤
STRAINED_ACHILLES=跟腱拉伤
STRAINED_CALF=小腿拉伤
STRAINED_ELBOW=肘部拉伤
STRAINED_GROIN=腹股沟拉伤
STRAINED_HAMSTRING=腿后肌群拉伤
STRAINED_HIP=髋部拉伤
STRAINED_MCL=内侧副韧带拉伤
STRAINED_NECK=颈部拉伤
STRAINED_OBLIQUE=腹斜肌拉伤
STRAINED_QUAD=股四头肌拉伤
SUSPENSION=停赛
THIGH_CRAMP=大腿抽筋
TORN_ACHILLES=跟腱撕裂
TORN_ACL=前交叉韧带撕裂
TORN_BICEP=肱二头肌撕裂
TORN_CALF_MUSCLE=小腿肌肉撕裂
TORN_HAMSTRING=腿后肌群撕裂
TORN_LABRUM=盂唇撕裂
TORN_LIGAMENT_ELBOW=肘部韧带撕裂
TORN_LIGAMENT_FOOT=足部韧带撕裂
TORN_LIGAMENT_HAND=手部韧带撕裂
TORN_MCL=内侧副韧带撕裂
TORN_MENISCUS=半月板撕裂
TORN_PATELLAR_TENDON=髌腱撕裂
TORN_TRICEP=肱三头肌撕裂
TWISTED_ANKLE=脚踝扭伤
TWISTED_KNEE=膝部扭伤
UNKNOWN_ABDOMEN_INJURY=腹部伤势未明
UNKNOWN_ANKLE_INJURY=脚踝伤势未明
UNKNOWN_ARM_INJURY=手臂伤势未明
UNKNOWN_BACK_INJURY=背部伤势未明
UNKNOWN_ELBOW_INJURY=肘部伤势未明
UNKNOWN_EYE_INJURY=眼部伤势未明
UNKNOWN_FINGER_INJURY=手指伤势未明
UNKNOWN_FOOT_INJURY=足部伤势未明
UNKNOWN_FOREARM_INJURY=前臂伤势未明
UNKNOWN_HAND_INJURY=手部伤势未明
UNKNOWN_HEAD_INJURY=头部伤势未明
UNKNOWN_HIP_INJURY=髋部伤势未明
UNKNOWN_ILLNESS=病情未明
UNKNOWN_KNEE_INJURY=膝部伤势未明
UNKNOWN_LEG_INJURY=腿部伤势未明
UNKNOWN_NECK_INJURY=颈部伤势未明
UNKNOWN_NOSE_INJURY=鼻部伤势未明
UNKNOWN_RIB_INJURY=肋部伤势未明
UNKNOWN_SHOULDER_INJURY=肩部伤势未明
UNKNOWN_STERNUM_INJURY=胸骨伤势未明
UNKNOWN_THIGH_INJURY=大腿伤势未明
UNKNOWN_TOE_INJURY=脚趾伤势未明
UNKNOWN_WRIST_INJURY=手腕伤势未明
WINDED_ABDOMEN=腹部受击岔气
Always=总是
Often=经常
Sometimes=有时
Rarely=很少
Never=从不
White=白色
Black=黑色
Red=红色
Blue=蓝色
Green=绿色
Yellow=黄色
Purple=紫色
Orange=橙色
Gray=灰色
Brown=棕色
Primary=主色
Secondary=辅色
Tertiary=第三颜色
Quaternary=第四颜色
Quintary=第五颜色
Uniform Primary Color=球衣主色
Uniform Armsleeve=球衣护臂色
Uniform Legsleeve=球衣护腿色
Arm Sleeve Color=护臂颜色
Leg Sleeve Color=护腿颜色
Uniform Color 1=球衣颜色一
Uniform Color 2=球衣颜色二
Uniform Color 3=球衣颜色三
Compression Shorts=紧身短裤
Compression Pants=紧身长裤
Short Sleeve=短袖
Long Sleeve=长袖
Shortsleeve Shirt=短袖内衬
Longsleeve Shirt=长袖内衬
Sleeve=护套
Padded Sleeve=加垫护套
Forearm Sleeve=前臂护套
Pad=护垫
Small Band=小护带
Medium Band=中护带
High Band=高护带
Short Wristband=短护腕
Long Wristband=长护腕
Rubberband=橡胶护腕
Cloth=布质护腕
Handwrap=缠手带
No Socks=不穿袜子
Quarter Socks=短袜
Crew Socks=中筒袜
Tall Socks=长袜
Nike=耐克
Adidas=阿迪达斯
Jordan=乔丹品牌
Converse=匡威
Reebok=锐步
Under Armour=安德玛
Spalding=斯伯丁
Puma=彪马
Anta=安踏
Li Ning=李宁
New Balance=新百伦
2K Generic=通用球鞋
Custom Made In-Game Face=游戏内自建面容
Cold=冷区
Neutral=普通区
Hot=热区
Lethal=致命热区
Low=低
Medium=中
High=高
Very High=非常高
Intense=激动
Calm=平静
Unrestricted=非受限
Restricted=受限
Team Option=球队选项
Player Option=球员选项
No Option=无选项
Steady=维持现状
Buying=补强
Selling=出售球员
Rebuilding=重建
Atlantic=大西洋赛区
Central=中央赛区
Southeast=东南赛区
Southwest=西南赛区
Northwest=西北赛区
Pacific=太平洋赛区
Allstar=全明星
Era Modern=现代
Era 10s=2010年代
Era 00s=2000年代
Era 90s=1990年代
Era 80s=1980年代
NORTH_AMERICA=北美
WESTERN_EUROPE=西欧
EASTERN_EUROPE=东欧
AFRICA=非洲
HISPANIC=西班牙语地区（HISPANIC）
ASIA=亚洲
`, choices);

type ChoiceField = Pick<FieldCell, 'key' | 'label' | 'kind' | 'options' | 'value' | 'display'>;

const emoteNames: Record<string, string> = {
    NONE: '无', WAVE: '挥手', FLEX: '展示肌肉', DOUBLEFLEX: '双臂展示肌肉',
    FAKEPASS: '传球假动作', QUICKFAKEPASS: '快速传球假动作', LAZYFAKEPASS: '慢速传球假动作',
    POINT: '指向前方', SPIN: '转身', SPINBALL: '转球', FINGERSPIN: '指尖转球',
    CROSSARMS: '交叉双臂', YAWN: '打哈欠', LAUGHING: '大笑', SHRUG: '耸肩',
    SHRUG2: '耸肩 2', QUICKSHRUG: '快速耸肩', DRYHANDS: '擦手', DRYHANDS2: '擦手 2',
    DRY1HAND: '单手擦拭', DROPBALL: '放下球', RAPIDDRIBBLE: '快速运球',
    AROUNDTHEBACK: '背后绕球', BALLFLIP: '翻球', CHESTSPIN: '胸前转球',
    LEGWRAP: '绕腿转球', NECKBALANCE: '颈部平衡球', TOSS: '抛球', UNDERLEG: '胯下动作',
    DISAPPOINTED: '失望', HIDE: '藏球', KISS: '飞吻', ROBOT: '机械舞',
    STOP: '停下手势', WAVEOFF: '摆手', SHOULDERSHIMMY: '抖肩',
    BIGBOUNCE: '大幅拍球', CANTHEARYOU: '听不见你', COLLAPSE: '倒地',
    DAMETIME: '利拉德时间', DANCEOFF: '斗舞', DIRECTTRAFFIC: '指挥跑位',
    DIRECTTRAFFIC2: '指挥跑位 2', DIRECTTRAFFIC3: '指挥跑位 3',
    HAPPYDANCE: '开心舞步', HAPPYSKIP: '欢快跳步', HUH: '疑问手势',
    IRISHJIG: '爱尔兰吉格舞', ISO: '单打手势', KICKCOMBO: '组合踢腿',
    KNEEBOUNCE: '膝部弹球', KNEEBOUNCEFAIL: '膝部弹球失败',
    LEAVEBALLPOINT: '放球指向', NAVIGATOR: '领航手势', NIGHTNIGHT: '晚安',
    PEEKABOO: '躲猫猫', PLAYCALL: '呼叫战术', PUMPCROWD: '带动观众',
    PUMPCROWD2: '带动观众 2', PUMPCROWD3: '带动观众 3',
    QUICKDRY: '快速擦手', SALSA: '萨尔萨舞', SHUFFLE: '滑步舞',
    SIGH: '叹气', SKIP: '跳步', SNOOZER: '打瞌睡', SPREAD: '展开双臂',
    SQUATSPIN: '下蹲转身', SUP: '打招呼', TAUNT: '挑衅', TURNAWAY: '转身离开',
    WIGGLE: '扭动', '2HFPASSFAKE': '双手传球假动作', AROUNDTHEBACKJAB: '背后绕球试探步',
    BACKWRAP: '背后绕球', CATCHERSTANCE: '接球手站姿', DAB: '达布舞',
    DOUBLETWEEN: '双重胯下动作', DUSTOFF: '掸灰', FIGHTME: '挑战手势',
    HEADER: '头球', HIGHUNDERLEGTOSS: '胯下高抛球', HIPWIGGLE: '扭胯',
    JWALL: '约翰·沃尔动作', LINEMARK: '划线手势', MJ_PALMBALL: '乔丹单手持球',
    MOVEBALL: '移动篮球', REALLY: '质疑手势', RUMPWRAP: '身后绕球',
    SHIMMY: '摇肩', SITONIT: '坐球', SURFER: '冲浪动作', TAKEIT: '拿球手势',
    TOSSANDJAB: '抛球试探步', TOSSAROUNDBACK: '背后抛球', TRICKS: '花式动作',
    WILDSHIMMY: '大幅摇肩',
};

const knownPlayerNames = new Set(Object.keys(webAvatarIndex.players).map((key) => key.split('|')[0]));
// Exact names also authored in dropdowns.json FACEID, PORTRAITID or AUDIOSIGNATUREID.
// Keep only names needed by action lists instead of bundling the complete schema.
for (const name of [
    'Rick Barry', 'Malik Beasley', 'Larry Bird', 'Bojan Bogdanovic', 'Jimmy Butler',
    'Bill Cartwright', 'Wilt Chamberlain', 'Marquese Chriss', 'Brandon Clarke', 'Nicolas Claxton',
    'Bob Cousy', 'Adrian Dantley', 'Zach Edey', 'Rick Fox', 'Jeff Hornacek', 'Magic Johnson',
    'Michael Jordan', 'Karl Malone', 'Alonzo Mourning', 'Don Nelson', 'Jahlil Okafor',
    'Glenn Robinson III', 'Nate Robinson', 'Ben Simmons', 'John Stockton', 'Jamaal Wilkes',
    'Kareem Abdul-Jabbar', 'Chauncey Billups', 'Reggie Bullock', 'Trey Burke', 'Carlton Carrington',
    'Michael Carter-Williams', 'Ed Davis', 'Johnny Davis', 'Matthew Dellavedova', 'Robert Dillingham',
    'Clyde Drexler', 'Kevin Duckworth', 'Wayne Ellington', 'Patrick Ewing', 'Derrick Favors',
    'Steve Francis', 'Johnny Furphy', 'Penny Hardaway', 'Willy Hernangomez', 'Ron Holland',
    'Richaun Holmes', "Nah'Shon Hyland", 'Allen Iverson', 'Reggie Jackson', 'Bronny James Jr.',
    'Stanley Johnson', 'Frank Kaminsky III', 'Steve Kerr', 'Toni Kukoc', 'Bill Laimbeer',
    'Bez Mbeng', 'Patrick Mills', 'Chris Mullin', 'Collin Murray-Boyles', 'Sviatoslav Mykhailiuk',
    'Raul Neto', 'Charles Oakley', 'Darryl Dawkins', 'Julius Erving', 'Gerald Green',
    'Shawn Kemp', 'Stephon Marbury', 'Latrell Sprewell', 'Dominique Wilkins', 'Alexandre Sarr',
    'Hakeem Olajuwon', 'Rafer Alston', 'Tim Hardaway', 'Josh Richardson', 'Dennis Rodman',
    'Christian Wood',
]) knownPlayerNames.add(normalize(name));
// Exact complete person-name tokens authored in the 2K27 action lists; SAM_P is unresolved.
for (const name of `
CAITLIN_CLARK CAMERON_REDDISH CANDACE_PARKER J_COLE MAITE_CAZORLA NICO_MANNION RONDAE_HOLLIS_JEFFERSON
DARIUS_ACUFF_JR NATE_AMENT CHRISTIAN_ANDERSON UDOKA_AZUBUIKE IBOU_BADJI MOHAMED_BAMBA
DICK_BARNETT KEITA_BATESDIOP DAMION_BAUGH KENT_BAZEMORE DARIUS_BAZLEY REECE_BEEKMAN
KHEM_BIRCH ONURALP_BITIM BOL_BOL LEANDRO_BOLMARO ISAAC_BONGA BRANDON_BOSTON_JR
JAMES_BOUKNIGHT IGNAS_BRAZDEIKIS JALEN_BRIDGES OSHAE_BRISSETT CHARLES_BROWN_JR GREG_BROWN
MIKEL_BROWN_JR STERLING_BROWN TROY_BROWN_JR BRAYDEN_BURRIES JARED_BUTLER DEVONTAE_CACOK
VLATKO_CANCAR VERNON_CAREY_JR CAMERON_CARR CHRIS_CENAC_JR CHRIS_CHIOZZA JOSH_CHRISTOPHER
GARY_CLARK TYLER_COOK JAVONTE_COOKE RICKY_COUNCIL_IV TORREY_CRAIG TERENCE_DAVIS
SERGIO_DE_LARREA DEWAYNE_DEDMON HAMIDOU_DIALLO PJ_DOZIER CHRIS_DUARTE CARSEN_EDWARDS
KESSLER_EDWARDS ZUBY_EJIOFOR CJ_ELLEBY BRUNO_FERNANDO YOGI_FERRELL KINGSTON_FLEMINGS
MALACHI_FLYNN BRYN_FORBES WENYEN_GABRIEL ANDERSSON_GARCIA USMAN_GARUBA HARRY_GILES_III
BRANDON_GOODWIN ALLEN_GRAVES JAMYCHAL_GREEN AJ_GRIFFIN KYLE_GUY YANG_HANSEN
MAURICE_HARKLESS JUAN_HERNANGOMEZ RODNEY_HOOD JALEN_HOODSCHIFINO TALEN_HORTONTUCKER DANUEL_HOUSE
ELIJAH_HUGHES ANDRE_IGUODALA FRANK_JACKSON JOSH_JACKSON JUSTIN_JACKSON JOSHUA_JEFFERSON
ALIZE_JOHNSON KEON_JOHNSON MOREZ_JOHNSON_JR TYLER_JOHNSON KAI_JONES MASON_JONES
LOUIS_KING NATHAN_KNIGHT KEVIN_KNOX_II FURKAN_KORKMAZ JEREMY_LAMB ROMEO_LANGFORD
JAKE_LAYMAN SABEN_LEE YAXEL_LENDEBORG KIRA_LEWIS_JR KENNETH_LOFTON_JR ROBIN_LOPEZ
THEO_MALEDON BOBAN_MARJANOVIC KELAN_MARTIN KENYON_MARTIN_JR SKYLAR_MAYS JALEN_MCDANIELS
JAVALE_MCGEE RODNEY_MCGRUDER BEN_MCLEMORE CHIMEZIE_METU SHAKE_MILTON JUSTIN_MINAYA
TAZE_MOORE MARCUS_MORRIS_SR MYCHAL_MULDER DAISHEN_NIX JAYLEN_NOWELL KENDRICK_NUNN
DAVID_NWABA JORDAN_NWORA SEMI_OJELEYE CHUMA_OKEKE EBUKA_OKORIE KZ_OKPALA
VICTOR_OLADIPO EUGENE_OMORUYI CEDI_OSMAN ERIC_PASCHALL KOA_PEAT
TERRY_ROZIER_III CAMERON_THOMAS LAUREN_BETTS BOBBY_PORTIS ROB_DILLINGHAM
BREANNA_STEWART AJA_WILSON ANTHONY_LAMB THEO_PINSON TREVELIN_QUEEN
ADMIRAL_SCHOFIELD COLE_SWIDER TERRY_TAYLOR JT_THOR JUAN_TOSCANO
LONNIE_WALKER_IV ROBERT_WILLIAMS JUSTISE_WINSLOW
`.trim().split(/\s+/)) knownPlayerNames.add(normalize(name));

const layupPlayerAliases: Record<string, string> = {
    LUP_G_GERVIN: 'George Gervin', LUP_A_IVERSON: 'Allen Iverson',
    LUP_M_JOHNSON: 'Magic Johnson', LUP_M_JORDAN: 'Michael Jordan',
    LUP_J_BUTLER: 'Jimmy Butler', LUP_B_SIMMONS: 'Ben Simmons',
};

// Translate explicit authored intro words without expanding player nicknames or initials.
const introNames: Record<string, string> = {
    PAOLO_RIMHANG: 'Paolo 挂筐', MELO_INSPECT_BALL: 'Melo 检查篮球',
    NJOKIC_THROWBALL: 'N. Jokic 抛球', JBRUNSON_INTRO: 'J. Brunson 开场动作',
    KG_HYPE_CROWD: 'KG 带动观众', DWIGHT_INTRO: 'Dwight 开场动作',
    LEBRON_INSPECT_BALL: 'LeBron 检查篮球', LEBRON_POWDER: 'LeBron 撒粉',
    KLAY_JUMPS: 'Klay 跳跃', BOSH_SPITS: 'Bosh 吐口水', CURRY_RUNS: 'Curry 跑动',
    ROSE_KISS: 'Rose 亲吻动作', CJMCCOLLUM_SWINGARMS: 'C. J. McCollum 摆臂',
    ADAMS_COLLISON_SHAKE: 'Adams / Collison 握手',
    BEAL_POINT_TO_SKY: 'Beal 指向天空', BOOKER_POINT_TO_SKY: 'Booker 指向天空',
    ANTETOKOUNMPO_LOOSEN_UP_V1: 'Antetokounmpo 放松活动 · 变体 1',
    ANTETOKOUNMPO_LOOSEN_UP_V2: 'Antetokounmpo 放松活动 · 变体 2',
    MURRAY_BLOWHANDS: 'Murray 吹手', SIAKAM_POINT_TO_SKY: 'Siakam 指向天空',
    YOUNG_SELF_PEP_TALK: 'Young 自我鼓励', OLADIPO_POINT_TO_SKY: 'Oladipo 指向天空',
    IRVING_LBJ_HUG: 'Irving / LBJ 拥抱', LBJ_SHUMPERT_SHAKE: 'LBJ / Shumpert 握手',
    LOVE_IRVING_SHAKE: 'Love / Irving 握手', SCURRY_JMCGEE_SHAKE: 'S. Curry / J. McGee 握手',
    JMCGEE_DGREEN_SHAKE: 'J. McGee / D. Green 握手',
    KTHOMPSON_JMCGEE_SHAKE: 'K. Thompson / J. McGee 握手',
    JMCGEE_KDURANT_SHAKE: 'J. McGee / K. Durant 握手',
    DGREEN_ICLARK_SHAKE: 'D. Green / I. Clark 握手',
    KIRVING_CFRYE_SHAKE: 'K. Irving / C. Frye 握手',
    DLILLARD_JNURKIC_SHAKE: 'D. Lillard / J. Nurkic 握手',
    RJACKSON_ABAYNES_SHAKE: 'R. Jackson / A. Baynes 握手',
    RLOPEZ_RMIROTIC_SHAKE: 'R. Lopez / R. Mirotic 握手',
    JHARDEN_CCAPELA_SHAKE: 'J. Harden / C. Capela 握手',
    JRSMITH_KIRVING_SHAKE: 'JR Smith / K. Irving 握手',
    LBJ_JRSMITH_SHAKE: 'LBJ / JR Smith 握手', LBJ_KLOVE_SHAKE: 'LBJ / K. Love 握手',
    LBJ_KIRVING_SHAKE: 'LBJ / K. Irving 握手', LBJ_TTHOMPSON_SHAKE: 'LBJ / T. Thompson 握手',
    JRSMITH_TTHOMPSON_SHAKE: 'JR Smith / T. Thompson 握手',
    CPAUL_JHARDEN_TUGOWAR_SHAKE: 'C. Paul / J. Harden 拔河式握手',
    JHARDEN_CCAPELA_HOPDANCE_SHAKE: 'J. Harden / C. Capela 跳舞握手',
    NJOKIC_DARTHUR_SEASONING_SHAKE: 'N. Jokic / D. Arthur 调味动作握手',
    SCURRY_BASELINESPRINT: 'S. Curry 底线冲刺', RWESTBROOK_CROWDSPRINT: 'R. Westbrook 向观众冲刺',
    JTATUM_POINT_EARRUB: 'J. Tatum 指向与揉耳', DBOOKER_POINTTOSKY: 'D. Booker 指向天空',
    JHARDEN_STANCHIONRUN: 'J. Harden 篮架跑动',
    GEN_RESUSCITATION: '心肺复苏动作', GEN_FLEXANDPOINT_SHAKE: '展示肌肉与指向握手',
    GEN_HIGHLOW_SHAKE: '高低位握手', GEN_EXCITED_SHAKE: '兴奋握手',
    GEN_MULTIPLEDAB_SHAKE: '多次达布舞握手', GEN_KARATE_SHAKE: '空手道握手',
    GEN_LEGKICKS_SHAKE: '踢腿握手', GEN_PATTYCAKE_SHAKE: '拍手游戏式握手',
    GEN_ONEHANDPROPER_SHAKE: '单手正式握手', GEN_TWOHANDPROPER_SHAKE: '双手正式握手',
    GEN_CHESTBUMP_SHAKE: '撞胸握手', GEN_ROCKPAPERSCISSOR_SHAKE: '石头剪刀布握手',
    GEN_THRICE_AND_DAB_SHAKE: '三次握手与达布舞', GEN_THRICE_AND_PRAY_SHAKE: '三次握手与祈祷',
    GEN_TRIPLE_SHAKE: '三重握手', GEN_TWICE_AND_FIGHT_SHAKE: '两次握手与打斗动作',
    GEN_2FRONT_2BACK_SHAKE: '前后各两次握手', GEN_UNCROSSARMS_SHAKE: '展开交叉双臂握手',
    GEN_BACKFLIP: '后空翻', GEN_BACKFLIP2: '后空翻 2', GEN_SHOW_BELT: '展示腰带',
    GEN_DANCE: '跳舞', GEN_BASEBALL: '棒球动作', GEN_PUSHUP: '俯卧撑',
    GEN_ROBOT: '机械舞', GEN_SALSA: '萨尔萨舞', GEN_HANDSTAND: '倒立',
    GEN_HYPECROWD_POINT: '带动观众 · 指向',
    GEN_HYPECROWD_CLAPWAVE: '带动观众 · 拍手挥手',
    GEN_HYPECROWD_HEARYOU: '带动观众 · 倾听手势', GEN_HYPECROWD_RUNBY: '带动观众 · 跑过',
    GEN_HYPECROWD_OURHOUSE: '带动观众 · 我们的主场',
    GEN_STANCH_DRUMS: '篮架动作 · 打鼓',
    GEN_STANCH_FASTPUNCH: '篮架动作 · 快速出拳', GEN_STANCH_FOCUS: '篮架动作 · 专注',
    GEN_STANCH_PUNCHKICK: '篮架动作 · 拳打脚踢', GEN_STANCH_LOWPUNCH: '篮架动作 · 低位出拳',
    GEN_STANCH_LEANBACK: '篮架动作 · 后仰',
    GEN_BALL_DANCE: '持球跳舞', GEN_BALL_ROUNDBACK: '篮球绕背',
    GEN_BALL_BOWLING: '篮球保龄球动作', GEN_BALL_BASEBALL: '篮球棒球动作',
    GEN_BALL_FOOTBALL: '篮球橄榄球动作', GEN_BALL_HITHEAD: '篮球碰头',
    GEN_DRIBBLE: '运球', GEN_PUNCHMITTS: '拳靶动作', GEN_BOXING: '拳击',
    GEN_BUMPJUMP: '跳起碰撞', GEN_FOOTGRAB: '抓脚', GEN_FISHING: '钓鱼动作',
    GEN_CAMERA: '拍照动作', GEN_PUNCHBAG: '击打沙袋',
    GENERIC_HANG_FLEX: '挂筐展示肌肉', GENERIC_HANG_SWING: '挂筐摆动',
    GENERIC_HANG_TAP: '挂筐轻拍', GEN_SHOVEOFF: '推开动作',
    GEN_FOOTBALL: '橄榄球动作', GEN_CATCHBUDDY: '接住同伴',
};

export function optionLabel(option: string, field?: Pick<ChoiceField, 'key' | 'label'>): string {
    const numbered = option.match(/^\[\d+\]\s*(.*)$/);
    const source = numbered?.[1] ?? option;
    if (field && normalize(source) === 'unknown') return '';
    if (field && /^(?:TEAM|OPPONENT|LASTYEARPLAYOFFSOPPONENT|TEAMINDEX|CHAMPIONTEAM)$/.test(field.key)
        && (source === '保留现有关联' || /^\d+ · \S.*$/.test(source))) return source;
    const emote = source.match(/^(?:EMDRB|EMTT)_(.+)$/);
    if (emote && /\/EMOTE(?:DRB|TT)$/.test(field?.key ?? '')) {
        return emoteNames[emote[1]] ?? '';
    }
    if (field?.key.startsWith('Signature/')) {
        const wnbaFreeThrow = /\/FREETHROW$/.test(field.key) && source.match(/^FREE_THROW_WNBA_([12])$/);
        if (wnbaFreeThrow) return `WNBA 罚球 ${wnbaFreeThrow[1]}`;
        if (/\/SIGPLAYERINTRO[12]$/.test(field.key) && introNames[source]) return introNames[source];
        if (/\/SIGJUMPBALL[^/]*$/.test(field.key)) {
            const jumpIntro = source.match(/^TIP_INTRO_V(0[1-9]|1\d|20)$/);
            if (jumpIntro) return `跳球开场动作 · 变体 ${jumpIntro[1]}`;
        }
        if (knownPlayerNames.has(normalize(source))) return source.replace(/_/g, ' ');
        const basic = source.match(/^(BASIC|NORMAL|PRO|ELITE)(?:_(WNBA|GUARD))?(?:_(\d+))?$/);
        if (basic) return `${({ BASIC: '基础', NORMAL: '普通', PRO: '职业', ELITE: '精英' })[basic[1]]}`
            + `${basic[2] === 'WNBA' ? ' WNBA' : basic[2] === 'GUARD' ? '后卫' : ''}${basic[3] ? ` ${basic[3]}` : ''}`;
        if (source === 'BIG_MAN') return '大个子';
        if (field.key.endsWith('/LAYUPPACKAGE')) {
            const defaults: Record<string, string> = {
                LUP_DEFAULT_SMALL: '默认小个子', LUP_DEFAULT_SWING: '默认侧翼', LUP_DEFAULT_BIG: '默认大个子',
                LUP_WNBA_SMALL: 'WNBA 小个子', LUP_WNBA_SWING: 'WNBA 侧翼', LUP_WNBA_BIG: 'WNBA 大个子',
            };
            if (defaults[source]) return defaults[source];
            if (layupPlayerAliases[source]) return layupPlayerAliases[source];
            const person = source.match(/^LUP_([A-Z])_([A-Z]+)$/);
            if (person && [...knownPlayerNames].some((name) =>
                name.startsWith(person[1].toLowerCase()) && name.endsWith(person[2].toLowerCase()))) {
                return `${person[1]}. ${person[2].replace(/JR$/, ' JR')}`;
            }
        }
        if (field.key.endsWith('/VOICETYPE')) {
            const voices: Record<string, string> = {
                BLACK_HIGH: '黑人声线 · 高音', BLACK_LOW: '黑人声线 · 低音',
                CAUCASIAN_HIGH: '白人声线 · 高音', CAUCASIAN_LOW: '白人声线 · 低音',
            };
            if (voices[source]) return voices[source];
        }
        if (/\/DUNKPACKAGE\d+$/.test(field.key)) {
            const category = source.match(/^(BLACKTOP|TRAMPOLINE|CONTACT_DUNK|UNLOCKABLE_DUNK)_(\d+)$/);
            if (category) return `${({ BLACKTOP: '街球扣篮', TRAMPOLINE: '蹦床扣篮',
                CONTACT_DUNK: '对抗扣篮', UNLOCKABLE_DUNK: '解锁扣篮' })[category[1]]} ${category[2]}`;
        }
    }
    const direct = choices.get(normalize(source)) ?? terms.get(normalize(source));
    if (direct) {
        return direct;
    }
    if (field?.key.endsWith('/CREATEDPLAYERNICKNAME')) {
        return source;
    }
    const years = source.match(/^(\d+)\s*Yrs?$/i);
    if (years) {
        return `${years[1]} 年`;
    }
    const femaleBody = source.match(/^BODYSHAPEFEMALE(\d+)$/i);
    if (femaleBody) {
        return `女性体型 ${Number(femaleBody[1])}`;
    }
    // An authored action identifier still needs a readable, defined label.
    const action = field?.key.startsWith('Signature/');
    if (action && !/DUNKPACKAGE|INTRO|EMOTE|RITUAL|SIGJUMPBALL|FREEFALLSTYLE/i.test(field?.key ?? '')) {
        const translatedValue = translated(source);
        return translatedValue ?? '';
    }
    if (numbered && /FACEID|HEADSHOTID|PORTRAITID|AUDIOSIGNATUREID/i.test(field?.key ?? '')) {
        return source;
    }
    const uniform = source.match(/^UNIFORM[_ ](?:COLOR[_ ])?(\d+)$/i);
    if (uniform) {
        return `球衣颜色 ${uniform[1]}`;
    }
    const body = source.match(/^(?:BODYSHAPE|BODYTYPE)[_ ]?(\d+)$/i);
    if (body) {
        return `体型 ${body[1]}`;
    }
    const readable = translated(source);
    if (readable) {
        return readable;
    }
    if (field && /DUNKPACKAGE/i.test(field.key)) {
        const dunkTerms: Record<string, string> = {
            onefoot: '单脚起跳', twofoot: '双脚起跳', onehand: '单手', twohand: '双手',
            underbasket: '篮下', rimpulls: '拉筐', rimgrazers: '轻扣', regular: '普通',
            athletic: '运动型', basic: '基础', reverse: '反手', reverses: '反手',
            windmill: '大风车', windmills: '大风车', tomahawk: '战斧', tomahawks: '战斧',
            baseline: '底线', clutch: '拉杆', clutches: '拉杆', quick: '快速',
            front: '正面', side: '侧面', back: '背身', scratchers: '脑后扣篮',
            straight: '直臂', arm: '手臂', hangs: '挂筐', contact: '对抗',
            bigman: '大个子', elite: '精英', pro: '职业', small: '小个子',
            off: '', lobs: '空接', alley: '空中', oop: '接力', rim: '篮筐',
            rimhangs: '挂筐', rimhang: '挂筐', double: '双重', cradle: '摇篮式',
            backscratchers: '脑后扣篮', leaning: '前倾', slams: '暴扣', slam: '暴扣',
            fist: '握拳', pump: '拉杆', pumps: '拉杆', switch: '换手',
            hand: '手', reverser: '反手', basicdunks: '基础扣篮', dunks: '扣篮',
            quickdrops: '快速下压', quickdropin: '快速放入', cockback: '后拉', uberathletic: '高难度',
            backscratcher: '脑后扣篮', blacktop: '街球扣篮',
            leans: '前倾', switcheroos: '换手', cradles: '摇篮式', wnba: 'WNBA', s: '',
        };
        let remaining = normalize(source);
        let result = '';
        const keys = Object.keys(dunkTerms).sort((a, b) => b.length - a.length);
        while (remaining) {
            const number = remaining.match(/^\d+/)?.[0];
            const key = keys.find((item) => remaining.startsWith(item));
            if (number) {
                result += ` ${number}`;
                remaining = remaining.slice(number.length);
            } else if (key) {
                result += dunkTerms[key];
                remaining = remaining.slice(key.length);
            } else {
                result = '';
                break;
            }
        }
        if (result) {
            return result;
        }
    }
    // Do not imply that an untranslated option's meaning has been verified.
    if (field) return '';
    return source.replace(/_/g, ' ').replace(/([a-z])([A-Z])/g, '$1 $2');
}

export function isConfirmedOption(option: string, field: Pick<ChoiceField, 'key' | 'label'>): boolean {
    return optionLabel(option, field).length > 0;
}

export function confirmedOptions(field: Pick<ChoiceField, 'key' | 'label' | 'options'>): string[] {
    return field.options.filter((option) => isConfirmedOption(option, field));
}

export function displayValue(field: ChoiceField, value = field.value): string {
    if (field.key === 'COMPACTYEAR' || field.key.endsWith('/COMPACTYEAR')) {
        return compactSeason(value);
    }
    if (field.key === 'SEASON') {
        return historySeason(value);
    }
    if (value === null || value === undefined) {
        return '未加载';
    }
    if (typeof value === 'boolean') {
        return value ? '是' : '否';
    }
    if (field.kind === 'choice') {
        return optionLabel(String(value), field);
    }
    if (typeof value === 'number') {
        return String(Math.round(value * 1000) / 1000);
    }
    return value;
}

export const journalLabel = (kind: string) => ({
    player: '球员编辑', players: '球员编辑', gs: '比赛设置', 'in-match-gs': '场内比赛设置',
    'player-edits': '球员编辑', 'team-edits': '球队编辑', 'roster-moves': '调队交易',
    presets: '比赛预设', exports: '球员备份', move: '调队', trade: '交易',
    '场内 GS': '当前比赛设置', '通用 GS': '通用比赛设置', GS: '比赛设置',
}[kind] ?? translated(kind) ?? '操作记录');

export function errorLabel(text: string): string {
    return text
        .replace(/Expected exactly one running NBA2K27\.exe/gi, '未检测到唯一的 NBA 2K27 进程，请启动游戏')
        .replace(/Invalid match object pointer/gi, '比赛对象尚未就绪或已变化，等待下一次刷新')
        .replace(/Match changed during reading; discard this sample and refresh/gi, '读取期间比赛数据发生变化，等待下一次刷新')
        .replace(/Match identity changed during reading/gi, '读取期间比赛对象发生变化，等待下一次刷新')
        .replace(/No supported match period is active/gi, '当前尚未进入可读取的比赛')
        .replace(/Match read budget exceeded/gi, '比赛数据超出读取预算，本次读取已停止')
        .replace(/NBA2K27\.exe is not running/gi, '游戏尚未运行')
        .replace(/Draft class is not loaded/gi, '新秀名单尚未加载')
        .replace(/(?:Game or roster changed|Game data changed[^;]*|Attachment changed)[^;]*/gi, '游戏或名单数据已变化，请重新读取')
        .replace(/is not assigned to a team/gi, '当前不属于任何球队')
        .replace(/current team address is not in the loaded Teams table/gi, '当前球队归属与已加载名单不一致')
        .replace(/slot (\d+)/g, '名单第 $1 位');
}
