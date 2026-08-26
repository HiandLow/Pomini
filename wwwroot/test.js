
        // State
        let isOpponentPerspective = false;
        let myParty = [];
        let opponentParty = [];
        let myActiveIndex = 0;
        let opponentActiveIndex = 0;
        let usageDataMap = {}; // Will hold data from /api/usage
        let masterDataMap = {};
        let masterMoveMap = {};
        
        let userSelectedItemMy = {}; 
        let userSelectedItemOpp = {}; 
        let userSelectedAbilityMy = {};
        let userSelectedAbilityOpp = {};
        let currentHpFractionMy = 1.0;
        let currentHpFractionOpp = 1.0;

        let myRanks = { atk: 0, def: 0, spa: 0, spd: 0, spe: 0 };
        let opponentRanks = { atk: 0, def: 0, spa: 0, spd: 0, spe: 0 };
        let currentWeather = "None"; // "Sun", "Rain", "Sand", "Snow"

        function formatRanks(ranks) {
            let parts = [];
            if (ranks.atk) parts.push(`공${ranks.atk>0?'+':''}${ranks.atk}`);
            if (ranks.def) parts.push(`방${ranks.def>0?'+':''}${ranks.def}`);
            if (ranks.spa) parts.push(`특공${ranks.spa>0?'+':''}${ranks.spa}`);
            if (ranks.spd) parts.push(`특방${ranks.spd>0?'+':''}${ranks.spd}`);
            if (ranks.spe) parts.push(`스핏${ranks.spe>0?'+':''}${ranks.spe}`);
            return parts.length > 0 ? parts.join(' ') : "-";
        }

        function applyRank(val, rank) {
            if (!rank) return val;
            if (rank > 0) return Math.floor(val * (2 + rank) / 2);
            if (rank < 0) return Math.floor(val * 2 / (2 - rank));
            return val;
        }

        // Status conditions: null | 'PRZ' | 'BRN' | 'PSN' | 'TOX' | 'SLP' | 'FRZ'
        let myStatus = null;
        let opponentStatus = null;

        const STATUS_LABEL = { PRZ: '마비', BRN: '화상', PSN: '독', TOX: '맹독', SLP: '잠듦', FRZ: '얼음' };
        const STAT_LABELS = { atk: '공격', def: '방어', spa: '특공', spd: '특방', spe: '스핏' };

        function buildStatusBoxHtml(ranks, status) {
            const statOrder = ['atk', 'def', 'spa', 'spd', 'spe'];
            let rows = statOrder.map(s => {
                const v = ranks[s];
                if (!v) return ``;
                const cls = v > 0 ? 'up' : 'down';
                const sign = v > 0 ? '+' : '';
                return `<span class="rank-badge ${cls}">${STAT_LABELS[s]} ${sign}${v}</span>`;
            }).join('');

            const stBadge = status
                ? `<span class="status-badge ${status.toLowerCase()}">${STATUS_LABEL[status] || status}</span>`
                : `<span class="status-badge none">-</span>`;

            const hasRanks = statOrder.some(s => ranks[s] !== 0);
            if (!hasRanks && !status) {
                return `<div class="sim-status-box" style="justify-content:center;"></div>`;
            }
            return `<div class="sim-status-box">
                ${stBadge}
                <div class="sim-status-label" style="margin-top:4px;">랭크</div>
                ${rows}
            </div>`;
        }

        const ALL_ABILITIES = ["강철술사", "강철정신", "거센턱", "건조피부", "검은철구", "근성", "급류", "내열", "노릇노릇바디", "단단한발톱", "독폭주", "두꺼운지방", "라이트메탈", "마중물", "맹화", "멀티스케일", "메가런처", "모래의힘", "무기력", "물", "바람타기", "바위나르기", "방음", "방진", "방탄", "벌레", "벌레의알림", "복슬복슬", "부유", "부자유친", "불가사의부적", "불꽃", "불꽃의갈기", "색안경", "선파워", "수포", "순수한힘", "스나이퍼", "스펙터가드", "심록", "얼음인분", "열폭주", "예리함", "옹골찬턱", "용의턱", "우격다짐", "의욕", "이상한비늘", "이판사판", "재앙의검", "재앙의구슬", "재앙의그릇", "재앙의목간", "저수", "적응력", "전기엔진", "정화의소금", "진홍빛고동", "천정부지", "천하장사", "철주먹", "초식", "축전", "타오르는불꽃", "테크니션", "트랜지스터", "퍼코트", "펑크록", "풀", "프리즘아머", "플라워기프트", "피뢰침", "필터", "하드록", "하드론엔진", "헤비메탈", "흙먹기"];

        const RESIST_BERRIES = {
            "오카열매": "FIRE", "꼬시개열매": "WATER", "초나열매": "ELECTRIC", "린드열매": "GRASS",
            "플카열매": "ICE", "로플열매": "FIGHTING", "으름열매": "POISON", "슈캐열매": "GROUND",
            "바코열매": "FLYING", "야파열매": "PSYCHIC", "리체열매": "BUG", "루미열매": "ROCK",
            "수불열매": "GHOST", "하반열매": "DRAGON", "마코열매": "DARK", "바리비열매": "STEEL",
            "로셀열매": "FAIRY", "카리열매": "NORMAL"
        };
        
        function getOpponentItem(oppActive, index) {
            if (userSelectedItemOpp[index] !== undefined) return userSelectedItemOpp[index];
            if (!oppActive) return null;
            const oppUsage = getUsageForDexId(oppActive.dexId || oppActive.DexId || oppActive.SpeciesId);
            if (oppUsage && oppUsage.items && oppUsage.items.length > 0) return oppUsage.items[0].ko;
            return null;
        }
        
        function getMyItem(p, index) {
            let key = (p && p.id) ? p.id : index;
            if (userSelectedItemMy[key] !== undefined) return userSelectedItemMy[key];
            if (p && p.ItemKo) return p.ItemKo;
            return "";
        }

        function getOpponentAbility(oppActive, index) {
            let key = (oppActive && oppActive.id) ? oppActive.id : index;
            if (userSelectedAbilityOpp[key] !== undefined) return userSelectedAbilityOpp[key];
            if (!oppActive) return "";
            const oppUsage = getUsageForDexId(oppActive.dexId || oppActive.DexId || oppActive.SpeciesId);
            if (oppUsage && oppUsage.abils && oppUsage.abils.length > 0) return oppUsage.abils[0].ko;
            return "";
        }

        function getMyAbility(p, index) {
            let key = (p && p.id) ? p.id : index;
            if (userSelectedAbilityMy[key] !== undefined) return userSelectedAbilityMy[key];
            if (p && p.AbilityKo) return p.AbilityKo;
            return "";
        }

        function applyItemModifiers(move, attackerItem, defenderItem) {
            let dmgMult = 1.0;
            let atkMult = 1.0;
            let defMult = 1.0;
            if (attackerItem) {
                if (attackerItem === "구애머리띠" && move.category === "PHYSICAL") atkMult *= 1.5;
                else if (attackerItem === "구애안경" && move.category === "SPECIAL") atkMult *= 1.5;
                else if (attackerItem === "생명의구슬") dmgMult *= 1.3;
                else if (attackerItem === "힘의머리띠" && move.category === "PHYSICAL") dmgMult *= 1.1;
                else if (attackerItem === "박식안경" && move.category === "SPECIAL") dmgMult *= 1.1;
                else if (attackerItem === "전기구슬") atkMult *= 2.0;
                else if (attackerItem === "실크스카프" && move.type === "NORMAL") dmgMult *= 1.2;
                else if (attackerItem === "목탄" && move.type === "FIRE") dmgMult *= 1.2;
                else if (attackerItem === "신비의물방울" && move.type === "WATER") dmgMult *= 1.2;
                else if (attackerItem === "기적의씨" && move.type === "GRASS") dmgMult *= 1.2;
                else if (attackerItem === "자석" && move.type === "ELECTRIC") dmgMult *= 1.2;
                else if (attackerItem === "부드러운모래" && move.type === "GROUND") dmgMult *= 1.2;
                else if (attackerItem === "용의이빨" && move.type === "DRAGON") dmgMult *= 1.2;
                else if (attackerItem === "검은띠" && move.type === "FIGHTING") dmgMult *= 1.2;
                else if (attackerItem === "독바늘" && move.type === "POISON") dmgMult *= 1.2;
                else if (attackerItem === "예리한부리" && move.type === "FLYING") dmgMult *= 1.2;
                else if (attackerItem === "휘어진스푼" && move.type === "PSYCHIC") dmgMult *= 1.2;
                else if (attackerItem === "은빛가루" && move.type === "BUG") dmgMult *= 1.2;
                else if (attackerItem === "딱딱한돌" && move.type === "ROCK") dmgMult *= 1.2;
                else if (attackerItem === "저주의부적" && move.type === "GHOST") dmgMult *= 1.2;
                else if (attackerItem === "검은안경" && move.type === "DARK") dmgMult *= 1.2;
                else if (attackerItem === "금속코트" && move.type === "STEEL") dmgMult *= 1.2;
                else if (attackerItem === "녹지않는얼음" && move.type === "ICE") dmgMult *= 1.2;
                else if (attackerItem === "요정의깃털" && move.type === "FAIRY") dmgMult *= 1.2;
            }
            if (defenderItem) {
                if (defenderItem === "돌격조끼" && move.category === "SPECIAL") defMult *= 1.5;
                if (defenderItem === "진화의휘석") defMult *= 1.5;
            }
            return { dmgMult, atkMult, defMult };
        }

        function applyAbilityModifiers(attackerStats, defenderStats, move, attackerTypes, defenderTypes, attackerAbility, defenderAbility, attackerHpFraction, defenderHpFraction, currentTypeEff) {
            let mods = { dmgMult: 1.0, atkMult: 1.0, defMult: 1.0, bpMult: 1.0 };
            
            if (attackerAbility) {
                if (attackerHpFraction <= 0.333334) {
                    if (attackerAbility === "급류" && move.type === "WATER") mods.atkMult *= 1.5;
                    if (attackerAbility === "심록" && move.type === "GRASS") mods.atkMult *= 1.5;
                    if (attackerAbility === "맹화" && move.type === "FIRE") mods.atkMult *= 1.5;
                    if (attackerAbility === "벌레의알림" && move.type === "BUG") mods.atkMult *= 1.5;
                }
                if (attackerAbility === "무기력" && attackerHpFraction <= 0.5) mods.atkMult *= 0.5;
                if (attackerAbility === "강철술사" && move.type === "STEEL") mods.atkMult *= 1.5;
                if (attackerAbility === "강철정신" && move.type === "STEEL") mods.bpMult *= 1.5;
                if (attackerAbility === "트랜지스터" && move.type === "ELECTRIC") mods.atkMult *= 1.3;
                if (attackerAbility === "용의턱" && move.type === "DRAGON") mods.atkMult *= 1.5;
                if (attackerAbility === "바위나르기" && move.type === "ROCK") mods.atkMult *= 1.5;
                if ((attackerAbility === "천하장사" || attackerAbility === "순수한힘" || attackerAbility === "요가파워") && move.category === "PHYSICAL") mods.atkMult *= 2.0;
                if (attackerAbility === "우격다짐" && move.power > 0) mods.bpMult *= 1.3;
                if (attackerAbility === "테크니션" && move.power <= 60) mods.bpMult *= 1.5;
                if (attackerAbility === "철주먹") mods.bpMult *= 1.2;
                if (attackerAbility === "거센턱" || attackerAbility === "옹골찬턱") mods.bpMult *= 1.5;
                if (attackerAbility === "예리함") mods.bpMult *= 1.5;
                if (attackerAbility === "펑크록") mods.bpMult *= 1.3;
                if (attackerAbility === "메가런처") mods.bpMult *= 1.5;
                if (attackerAbility === "적응력" && attackerTypes.includes(move.type)) mods.dmgMult *= (2.0 / 1.5);
                if (attackerAbility === "이판사판") mods.bpMult *= 1.2;
                if (attackerAbility === "단단한발톱") mods.bpMult *= 1.3;
                if ((attackerAbility === "독폭주" || attackerAbility === "근성" || attackerAbility === "열폭주" || attackerAbility === "의욕") && move.category === "PHYSICAL") mods.atkMult *= 1.5;
                if (attackerAbility === "선파워" && move.category === "SPECIAL") mods.atkMult *= 1.5;
                if (attackerAbility === "색안경" && currentTypeEff < 1) mods.dmgMult *= 2.0;
                if (attackerAbility === "수포" && move.type === "WATER") mods.bpMult *= 2.0;
            }

            if (defenderAbility) {
                if ((defenderAbility === "저수" || defenderAbility === "마중물" || defenderAbility === "건조피부") && move.type === "WATER") mods.dmgMult = 0;
                if ((defenderAbility === "축전" || defenderAbility === "전기엔진" || defenderAbility === "피뢰침") && move.type === "ELECTRIC") mods.dmgMult = 0;
                if (defenderAbility === "타오르는불꽃" && move.type === "FIRE") mods.dmgMult = 0;
                if (defenderAbility === "초식" && move.type === "GRASS") mods.dmgMult = 0;
                if ((defenderAbility === "흙먹기" || defenderAbility === "부유") && move.type === "GROUND") mods.dmgMult = 0;
                if (defenderAbility === "두꺼운지방" && (move.type === "FIRE" || move.type === "ICE")) mods.atkMult *= 0.5;
                if (defenderAbility === "수포" && move.type === "FIRE") mods.dmgMult *= 0.5;
                if (defenderAbility === "내열" && move.type === "FIRE") mods.dmgMult *= 0.5;
                if (defenderAbility === "정화의소금" && move.type === "GHOST") mods.dmgMult *= 0.5;
                if (defenderAbility === "건조피부" && move.type === "FIRE") mods.dmgMult *= 1.25;
                if (defenderAbility === "복슬복슬") { if (move.type === "FIRE") mods.dmgMult *= 2.0; mods.dmgMult *= 0.5; }
                if (defenderAbility === "퍼코트" && move.category === "PHYSICAL") mods.defMult *= 2.0;
                if (defenderAbility === "얼음인분" && move.category === "SPECIAL") mods.dmgMult *= 0.5;
                if (defenderAbility === "이상한비늘" && move.category === "PHYSICAL") mods.defMult *= 1.5;
                if ((defenderAbility === "하드록" || defenderAbility === "필터" || defenderAbility === "프리즘아머") && currentTypeEff > 1) mods.dmgMult *= 0.75;
                if (defenderHpFraction >= 1.0 && (defenderAbility === "멀티스케일" || defenderAbility === "스펙터가드")) mods.dmgMult *= 0.5;
            }

            if (attackerAbility === "재앙의검" && move.category === "PHYSICAL") mods.defMult *= 0.75;
            if (attackerAbility === "재앙의구슬" && move.category === "SPECIAL") mods.defMult *= 0.75;
            if (defenderAbility === "재앙의목간" && move.category === "PHYSICAL") mods.atkMult *= 0.75;
            if (defenderAbility === "재앙의그릇" && move.category === "SPECIAL") mods.atkMult *= 0.75;

            return mods;
        }

        const TYPE_CHART = {
            NORMAL: { ROCK: 0.5, GHOST: 0, STEEL: 0.5 },
            FIRE: { FIRE: 0.5, WATER: 0.5, GRASS: 2, ICE: 2, BUG: 2, ROCK: 0.5, DRAGON: 0.5, STEEL: 2 },
            WATER: { FIRE: 2, WATER: 0.5, GRASS: 0.5, GROUND: 2, ROCK: 2, DRAGON: 0.5 },
            ELECTRIC: { WATER: 2, ELECTRIC: 0.5, GRASS: 0.5, GROUND: 0, FLYING: 2, DRAGON: 0.5 },
            GRASS: { FIRE: 0.5, WATER: 2, GRASS: 0.5, POISON: 0.5, GROUND: 2, FLYING: 0.5, BUG: 0.5, ROCK: 2, DRAGON: 0.5, STEEL: 0.5 },
            ICE: { FIRE: 0.5, WATER: 0.5, GRASS: 2, ICE: 0.5, GROUND: 2, FLYING: 2, DRAGON: 2, STEEL: 0.5 },
            FIGHTING: { NORMAL: 2, ICE: 2, POISON: 0.5, FLYING: 0.5, PSYCHIC: 0.5, BUG: 0.5, ROCK: 2, GHOST: 0, DARK: 2, STEEL: 2, FAIRY: 0.5 },
            POISON: { GRASS: 2, POISON: 0.5, GROUND: 0.5, ROCK: 0.5, GHOST: 0.5, STEEL: 0, FAIRY: 2 },
            GROUND: { FIRE: 2, ELECTRIC: 2, GRASS: 0.5, POISON: 2, FLYING: 0, BUG: 0.5, ROCK: 2, STEEL: 2 },
            FLYING: { ELECTRIC: 0.5, GRASS: 2, FIGHTING: 2, BUG: 2, ROCK: 0.5, STEEL: 0.5 },
            PSYCHIC: { FIGHTING: 2, POISON: 2, PSYCHIC: 0.5, DARK: 0, STEEL: 0.5 },
            BUG: { FIRE: 0.5, GRASS: 2, FIGHTING: 0.5, POISON: 0.5, FLYING: 0.5, PSYCHIC: 2, GHOST: 0.5, DARK: 2, STEEL: 0.5, FAIRY: 0.5 },
            ROCK: { FIRE: 2, ICE: 2, FIGHTING: 0.5, GROUND: 0.5, FLYING: 2, BUG: 2, STEEL: 0.5 },
            GHOST: { NORMAL: 0, PSYCHIC: 2, GHOST: 2, DARK: 0.5 },
            DRAGON: { DRAGON: 2, STEEL: 0.5, FAIRY: 0 },
            DARK: { FIGHTING: 0.5, PSYCHIC: 2, GHOST: 2, DARK: 0.5, FAIRY: 0.5 },
            STEEL: { FIRE: 0.5, WATER: 0.5, ELECTRIC: 0.5, ICE: 2, ROCK: 2, STEEL: 0.5 },
            FAIRY: { FIRE: 0.5, FIGHTING: 2, POISON: 0.5, DRAGON: 2, DARK: 2, STEEL: 0.5 }
        };

        function getTypeEffectiveness(moveType, targetTypes) {
            if (!moveType || !targetTypes || targetTypes.length === 0) return 1.0;
            let eff = 1.0;
            const effMap = TYPE_CHART[moveType.toUpperCase()];
            if (effMap) {
                targetTypes.forEach(t => {
                    const tUpper = t.toUpperCase();
                    if (effMap[tUpper] !== undefined) eff *= effMap[tUpper];
                });
            }
            return eff;
        }

        function calculateDamage(attackerStats, defenderStats, move, attackerTypes, defenderTypes, attackerItem, defenderItem, attackerAbility, defenderAbility, attackerHpFraction, defenderHpFraction) {
            if (!move || move.category === 'STATUS' || move.power === 0) return { minRaw: 0, maxRaw: 0, min: '0.0', max: '0.0' };
            
            let atkStat = move.category === 'PHYSICAL' ? attackerStats.atk : attackerStats.spa;
            let defStat = move.category === 'PHYSICAL' ? defenderStats.def : defenderStats.spd;
            if (!atkStat || !defStat || !defenderStats.hp) return { minRaw: 0, maxRaw: 0, min: '0.0', max: '0.0', minScalar: 0, maxScalar: 0 };
            
            const mods = applyItemModifiers(move, attackerItem, defenderItem);
            const eff = getTypeEffectiveness(move.type, defenderTypes);
            const aMods = applyAbilityModifiers(attackerStats, defenderStats, move, attackerTypes, defenderTypes, attackerAbility, defenderAbility, attackerHpFraction, defenderHpFraction, eff);
            
            atkStat = Math.floor(atkStat * mods.atkMult * aMods.atkMult);
            defStat = Math.floor(defStat * mods.defMult * aMods.defMult);

            let basePower = Math.floor(move.power * aMods.bpMult);
            let baseDamage = Math.floor(Math.floor(Math.floor(22) * basePower * atkStat / defStat) / 50) + 2;

            if (attackerTypes.some(t => t.toUpperCase() === move.type.toUpperCase())) {
                baseDamage = Math.floor(baseDamage * 1.5);
            }

            baseDamage = Math.floor(baseDamage * eff);
            if (eff === 0) return { minRaw: 0, maxRaw: 0, min: '0.0', max: '0.0', minScalar: 0, maxScalar: 0 };

            const minDmg = Math.floor(baseDamage * 0.85 * mods.dmgMult * aMods.dmgMult);
            const maxDmg = Math.floor(baseDamage * 1.00 * mods.dmgMult * aMods.dmgMult);

            const minRaw = minDmg / defenderStats.hp * 100;
            const maxRaw = maxDmg / defenderStats.hp * 100;

            return {
                minRaw: minRaw,
                maxRaw: maxRaw,
                min: minRaw.toFixed(1),
                max: maxRaw.toFixed(1),
                minScalar: minDmg,
                maxScalar: maxDmg
            };
        }

        function getOpponentStats(baseStats, isMax, isNaturePlus) {
            const hp = Math.floor((baseStats.hp * 2 + 31) / 2) + 60 + (isMax ? 32 : 0);
            const getStat = (base) => Math.floor((Math.floor((base * 2 + 31) / 2) + 5 + (isMax ? 32 : 0)) * (isNaturePlus ? 1.1 : 1.0));
            return {
                hp: hp,
                atk: getStat(baseStats.atk),
                def: getStat(baseStats.def),
                spa: getStat(baseStats.spa),
                spd: getStat(baseStats.spd),
                spe: getStat(baseStats.spe)
            };
        }

        function getMyActualStats(myActive, baseStats) {
            const evs = myActive.Evs || {};
            const hp = Math.floor((baseStats.hp * 2 + 31) / 2) + 60 + (evs["Hp"] || 0);
            const getStat = (base, key, isPlus, isMinus) => {
                let val = Math.floor((base * 2 + 31) / 2) + 5 + (evs[key] || 0);
                let mult = 1.0;
                if (isPlus) mult = 1.1;
                else if (isMinus) mult = 0.9;
                return Math.floor(val * mult);
            };
            const n = myActive.NatureKo || "";
            const natureMap = {
                "외로운": ["Atk", "Def"], "용감": ["Atk", "Spe"], "고집": ["Atk", "Spa"], "개구쟁이": ["Atk", "Spd"],
                "대담": ["Def", "Atk"], "무사태평": ["Def", "Spe"], "장난꾸러기": ["Def", "Spa"], "촐랑": ["Def", "Spd"],
                "겁쟁이": ["Spe", "Atk"], "성급": ["Spe", "Def"], "명랑": ["Spe", "Spa"], "천진난만": ["Spe", "Spd"],
                "조심": ["Spa", "Atk"], "의젓": ["Spa", "Def"], "냉정": ["Spa", "Spe"], "덜렁": ["Spa", "Spd"],
                "차분": ["Spd", "Atk"], "얌전": ["Spd", "Def"], "건방": ["Spd", "Spe"], "신중": ["Spd", "Spa"]
            };
            let plus = null, minus = null;
            if (natureMap[n]) { plus = natureMap[n][0]; minus = natureMap[n][1]; }
            return {
                hp: hp,
                atk: getStat(baseStats.atk, "Atk", plus === "Atk", minus === "Atk"),
                def: getStat(baseStats.def, "Def", plus === "Def", minus === "Def"),
                spa: getStat(baseStats.spa, "Spa", plus === "Spa", minus === "Spa"),
                spd: getStat(baseStats.spd, "Spd", plus === "Spd", minus === "Spd"),
                spe: getStat(baseStats.spe, "Spe", plus === "Spe", minus === "Spe")
            };
        }

        function getSpeeds(baseSpe, item) {
            if (!baseSpe) return { max: 'ERR', semi: 'ERR', uninvested: 'ERR' };
            const uninvested = Math.floor((baseSpe * 2 + 31) / 2) + 5;
            const semi = Math.floor((baseSpe * 2 + 31 + 63) / 2) + 5;
            const max = Math.floor(semi * 1.1);
            
            let mult = (item === "구애스카프") ? 1.5 : 1.0;
            return {
                max: Math.floor(max * mult),
                semi: Math.floor(semi * mult),
                uninvested: Math.floor(uninvested * mult)
            };
        }

        function getMyActualSpeed(myActive, item) {
            if (!myActive) return "-";
            let spe = 0;
            if (myActive.Stats && myActive.Stats.Spe) spe = myActive.Stats.Spe;
            else if (myActive.stats && myActive.stats.spe) spe = myActive.stats.spe;
            else {
                const dexId = myActive.SpeciesId || myActive.dexId;
                const masterInfo = masterDataMap[dexId];
                if (!masterInfo || !masterInfo.baseStats) return "-";
                
                const baseSpe = masterInfo.baseStats.spe;
                const uninvested = Math.floor((baseSpe * 2 + 31) / 2) + 5;
                const evBump = (myActive.Evs && myActive.Evs.Spe) ? myActive.Evs.Spe : 0;
                
                let natureMult = 1.0;
                const nature = myActive.NatureKo || "";
                if (["명랑", "겁쟁이", "천진난만", "성급"].includes(nature)) natureMult = 1.1;
                else if (["용감", "무사태평", "냉정", "건방"].includes(nature)) natureMult = 0.9;
                
                spe = Math.floor((uninvested + evBump) * natureMult);
            }
            if (item === "구애스카프") return Math.floor(spe * 1.5);
            return spe;
        }

        // Fetch Usage Data
        fetch("/api/usage")
            .then(res => res.json())
            .then(data => {
                usageDataMap = data;
                renderAll(); // Re-render if usage data arrives late
            })
            .catch(e => console.error("Failed to load usage data", e));

        // Fetch Master Data
        fetch("/api/master")
            .then(res => res.json())
            .then(data => {
                if (data && data.species) {
                    data.species.forEach(p => {
                        masterDataMap[p.id] = p;
                    });
                }
                if (data && data.moves) {
                    data.moves.forEach(m => {
                        masterMoveMap[m.nameKo] = m;
                    });
                }
                renderAll();
            })
            .catch(e => console.error("Failed to load master data", e));

        const toggleBtn = document.getElementById("togglePerspectiveBtn");
        toggleBtn.addEventListener("click", () => {
            isOpponentPerspective = !isOpponentPerspective;
            if (isOpponentPerspective) {
                toggleBtn.textContent = "상대 시점 ✔";
                toggleBtn.classList.add("active");
            } else {
                toggleBtn.textContent = "상대";
                toggleBtn.classList.remove("active");
            }
            renderAll();
        });

        function getTypesHtml(typesArr) {
            if (!typesArr) return "";
            if (typeof typesArr === 'string') typesArr = [typesArr];
            if (!Array.isArray(typesArr)) return "";
            return typesArr.map(t => `<img src="/img/types/${t.toLowerCase()}.png" alt="${t}" onerror="this.style.display='none'">`).join('');
        }

        // Helper to find usage data by dexId
        function getUsageForDexId(dexId) {
            if (!usageDataMap || !dexId) return null;

            // Search deeply in the JSON structure for an object with the matching dexId
            let found = null;
            function searchObj(obj) {
                if (found || !obj) return;
                if (Array.isArray(obj)) {
                    for (let i = 0; i < obj.length; i++) {
                        if (obj[i] && obj[i].dexId == dexId) { found = obj[i]; return; }
                        if (typeof obj[i] === 'object') searchObj(obj[i]);
                    }
                } else if (typeof obj === 'object') {
                    if (obj.dexId == dexId) { found = obj; return; }
                    for (let k in obj) {
                        if (typeof obj[k] === 'object') searchObj(obj[k]);
                    }
                }
            }
            searchObj(usageDataMap);
            return found;
        }

        // Helper to get top moves from usage
        function getTopMoves(usageObj, count = 4) {
            if (usageObj && usageObj.moves && usageObj.moves.length > 0) {
                return usageObj.moves.slice(0, count).map(m => m.ko);
            }
            return ["-", "-", "-", "-"];
        }

        // Helper to format opponent EV array
        function formatEV(evArr) {
            if (!evArr || evArr.length !== 6) return "알 수 없음";
            const labels = ["H", "A", "B", "C", "D", "S"];
            let res = [];
            for (let i = 0; i < 6; i++) {
                if (evArr[i] > 0) {
                    res.push(labels[i] + evArr[i]);
                }
            }
            return res.join(" ") || "무보정";
        }

        // Format my EV object {"Hp": 32, "Atk": 25, ...}
        function formatMyEV(evsObj) {
            if (!evsObj) return "알 수 없음";
            const keys = ["Hp", "Atk", "Def", "Spa", "Spd", "Spe"];
            const labels = ["H", "A", "B", "C", "D", "S"];
            let res = [];
            for (let i = 0; i < 6; i++) {
                let v = evsObj[keys[i]];
                if (v > 0) {
                    res.push(labels[i] + v);
                }
            }
            return res.join(" ") || "무보정";
        }

        // Dummy Master Data mapping
        const dummyNames = { 428: "이어롭", 36: "픽시", 1000: "타부자고", 445: "한카리아스", 887: "드래펄트", 130: "갸라도스", 937: "파블르", 212: "핫삼", 475: "엘레이드", 908: "마스카나", 730: "누리레느", 448: "루카리오" };

        let currentMyHpText = "HP - / -";
        let currentOpponentHpText = "HP -%";

        function renderAll() {
            try {
                renderTopGrid();
                renderSimContent();
                renderSmallParties();
                renderDetailPanels();
            } catch (e) {
                document.getElementById("simContent").innerHTML = `<div style="color:red; padding:20px;">Render Error: ${e.message}<br>${e.stack}</div>`;
            }
        }

        function renderTopGrid() {
            const grid = document.getElementById("topPartyGrid");
            const title = document.getElementById("topGridTitle");
            grid.innerHTML = "";

            let displayParty = isOpponentPerspective ? myParty : opponentParty;

            title.textContent = isOpponentPerspective ? "내 파티 (상대 기준 위력 — 상대가 공격)" : "상대 파티 (내 현재 포켓몬 기준 위력)";

            for (let i = 0; i < 6; i++) {
                if (i < displayParty.length) {
                    const p = displayParty[i];

                    let movesList = ["-", "-", "-", "-"];
                    if (isOpponentPerspective) {
                        // Showing MY party's moves. My party has `MovesKo`.
                        const oppActive = opponentParty[opponentActiveIndex]; // But we show opponent's active Pokemon's moves?
                        // Wait, "상대 파티 (내 현재 포켓몬 기준 위력)" => My active Pokemon's moves towards Opponent Party.
                        // "내 파티 (상대 기준 위력)" => Opponent active Pokemon's moves towards My Party.
                        if (oppActive) {
                            let oppUsage = getUsageForDexId(oppActive.dexId || oppActive.DexId || oppActive.SpeciesId);
                            movesList = getTopMoves(oppUsage);
                        }
                    } else {
                        // My Perspective => Show MY active Pokemon's moves towards Opponent Party.
                        const myActive = myParty[myActiveIndex];
                        if (myActive && myActive.MovesKo) {
                            movesList = myActive.MovesKo;
                        }
                    }

                    let movesHtml = "";
                    for (let m = 0; m < 4; m++) {
                        let moveName = movesList[m] || "-";
                        let dmgHtml = `<span class="move-dmg">0%</span>`;
                        if (moveName !== "-" && masterMoveMap[moveName]) {
                            const move = masterMoveMap[moveName];

                            const myActive = myParty[myActiveIndex];
                            const oppActive = opponentParty[opponentActiveIndex];

                            if (isOpponentPerspective) {
                                // Attacker: Opponent Active, Defender: My Party Pokemon (p)
                                if (oppActive && p) {
                                    const oppBaseStats = masterDataMap[oppActive.SpeciesId || oppActive.dexId]?.baseStats;
                                    const myBaseStats = masterDataMap[p.SpeciesId || p.dexId]?.baseStats;
                                    if (oppBaseStats && myBaseStats) {
                                        const defStats = getMyActualStats(p, myBaseStats);
                                        const atkMax = getOpponentStats(oppBaseStats, true, true);
                                        const atkSemi = getOpponentStats(oppBaseStats, true, false);
                                        const atkZero = getOpponentStats(oppBaseStats, false, false);
                                        const oppTypes = oppActive.types || oppActive.Types || (masterDataMap[oppActive.SpeciesId || oppActive.dexId]?.types) || ["NORMAL"];
                                        const myTypes = p.types || p.Types || p.type || (masterDataMap[p.SpeciesId || p.dexId]?.types) || ["NORMAL"];
                                        
                                        const oppItem = getOpponentItem(oppActive, opponentActiveIndex);
                                        const myItem = getMyItem(p, i);
                                        const oppAbility = getOpponentAbility(oppActive, opponentActiveIndex);
                                        const myAbility = getMyAbility(p, i);

                                        const dMax = calculateDamage(atkMax, defStats, move, oppTypes, myTypes, oppItem, myItem, oppAbility, myAbility, currentHpFractionOpp, currentHpFractionMy);
                                        const dSemi = calculateDamage(atkSemi, defStats, move, oppTypes, myTypes, oppItem, myItem, oppAbility, myAbility, currentHpFractionOpp, currentHpFractionMy);
                                        const dZero = calculateDamage(atkZero, defStats, move, oppTypes, myTypes, oppItem, myItem, oppAbility, myAbility, currentHpFractionOpp, currentHpFractionMy);

                                        const isPhys = move.category === 'PHYSICAL';
                                        const isSpec = move.category === 'SPECIAL';
                                        const maxClass = isPhys ? 'phys-max' : (isSpec ? 'spec-max' : 'zero');
                                        const semiClass = isPhys ? 'phys-semi' : (isSpec ? 'spec-semi' : 'zero');

                                        dmgHtml = `<div class="move-dmg-boxes">
                                            <span class="move-dmg-box ${maxClass}" title="공/특공 극보정">${dMax.minRaw === 0 ? '0' : Math.floor(dMax.minScalar) + '~' + Math.floor(dMax.maxScalar)}</span>
                                            <span class="move-dmg-box ${semiClass}" title="공/특공 252보정">${dSemi.minRaw === 0 ? '0' : Math.floor(dSemi.minScalar) + '~' + Math.floor(dSemi.maxScalar)}</span>
                                            <span class="move-dmg-box zero" title="공/특공 무보정">${dZero.minRaw === 0 ? '0' : Math.floor(dZero.minScalar) + '~' + Math.floor(dZero.maxScalar)}</span>
                                        </div>`;
                                    }
                                }
                            } else {
                                // Attacker: My Active, Defender: Opponent Party Pokemon (p)
                                if (myActive && p) {
                                    const myBaseStats = masterDataMap[myActive.SpeciesId || myActive.dexId]?.baseStats;
                                    const oppBaseStats = masterDataMap[p.SpeciesId || p.dexId]?.baseStats;
                                    if (myBaseStats && oppBaseStats) {
                                        const atkStats = getMyActualStats(myActive, myBaseStats);
                                        const defMax = getOpponentStats(oppBaseStats, true, true);
                                        const defSemi = getOpponentStats(oppBaseStats, true, false);
                                        const defZero = getOpponentStats(oppBaseStats, false, false);
                                        const myTypes = myActive.types || myActive.Types || myActive.type || (masterDataMap[myActive.SpeciesId || myActive.dexId]?.types) || ["NORMAL"];
                                        const oppTypes = p.types || p.Types || (masterDataMap[p.SpeciesId || p.dexId]?.types) || ["NORMAL"];
                                        
                                        const myItem = getMyItem(myActive, myActiveIndex);
                                        const oppItem = getOpponentItem(p, i);
                                        const myAbility = getMyAbility(myActive, myActiveIndex);
                                        const oppAbility = getOpponentAbility(p, i);

                                        const dMax = calculateDamage(atkStats, defMax, move, myTypes, oppTypes, myItem, oppItem, myAbility, oppAbility, currentHpFractionMy, currentHpFractionOpp);
                                        const dSemi = calculateDamage(atkStats, defSemi, move, myTypes, oppTypes, myItem, oppItem, myAbility, oppAbility, currentHpFractionMy, currentHpFractionOpp);
                                        const dZero = calculateDamage(atkStats, defZero, move, myTypes, oppTypes, myItem, oppItem, myAbility, oppAbility, currentHpFractionMy, currentHpFractionOpp);

                                        const isPhys = move.category === 'PHYSICAL';
                                        const isSpec = move.category === 'SPECIAL';
                                        const maxClass = isPhys ? 'phys-max' : (isSpec ? 'spec-max' : 'zero');
                                        const semiClass = isPhys ? 'phys-semi' : (isSpec ? 'spec-semi' : 'zero');

                                        dmgHtml = `<div class="move-dmg-boxes">
                                            <span class="move-dmg-box ${maxClass}" title="내구 극보정">${dMax.minRaw === 0 ? '0%' : Math.floor(dMax.minRaw) + '~' + Math.floor(dMax.maxRaw) + '%'}</span>
                                            <span class="move-dmg-box ${semiClass}" title="체력만 보정">${dSemi.minRaw === 0 ? '0%' : Math.floor(dSemi.minRaw) + '~' + Math.floor(dSemi.maxRaw) + '%'}</span>
                                            <span class="move-dmg-box zero" title="내구 무보정">${dZero.minRaw === 0 ? '0%' : Math.floor(dZero.minRaw) + '~' + Math.floor(dZero.maxRaw) + '%'}</span>
                                        </div>`;
                                    }
                                }
                            }
                        }
                        movesHtml += `<div class="move-row"><span class="move-name">${moveName}</span>${dmgHtml}</div>`;
                    }

                    let iconFile = p.iconFile || (String(p.SpeciesId || p.dexId).padStart(4, '0') + "_default.png");
                    let name = p.name || p.NameKo || dummyNames[p.SpeciesId || p.dexId] || "포켓몬";
                    let types = p.types || p.Types || (masterDataMap[p.SpeciesId || p.dexId]?.types) || ["NORMAL"];

                    let baseSpe = masterDataMap[p.SpeciesId || p.dexId] ? masterDataMap[p.SpeciesId || p.dexId].baseStats.spe : 0;
                    let speeds = getSpeeds(baseSpe);

                    grid.innerHTML += `
                        <div class="party-card">
                            <div class="moves-col">${movesHtml}</div>
                            <div class="pokemon-col">
                                <img class="sprite" src="/img/pokemon/${iconFile}" onerror="this.src=''; this.alt='?';">
                                <div class="types">${getTypesHtml(types)}</div>
                                <div class="name">${name}</div>
                                <div class="stats">
                                    <div class="stat-box hp">${speeds.max || '000'}</div>
                                    <div class="stat-box atk">${speeds.semi || '000'}</div>
                                    <div class="stat-box def">${speeds.uninvested || '000'}</div>
                                </div>
                            </div>
                        </div>
                    `;
                } else {
                    grid.innerHTML += `<div class="party-card" style="justify-content:center; align-items:center; color:var(--text-muted)">비어있음</div>`;
                }
            }
        }

        function renderSimContent() {
            const simContent = document.getElementById("simContent");
            const myActive = myParty[myActiveIndex];
            const oppActive = opponentParty[opponentActiveIndex];

            if (!myActive && !oppActive) {
                simContent.innerHTML = "";
                return;
            }

            const myIcon = myActive ? (String(myActive.SpeciesId).padStart(4, '0') + "_default.png") : "";
            const myName = myActive ? (dummyNames[myActive.SpeciesId] || "내 포켓몬") : "";
            const myMoves = myActive ? myActive.MovesKo : ["-", "-", "-", "-"];

            const oppIcon = oppActive ? oppActive.iconFile : "";
            const oppName = oppActive ? oppActive.name : "상대 포켓몬";

            let oppUsage = oppActive ? getUsageForDexId(oppActive.dexId || oppActive.DexId || oppActive.SpeciesId) : null;
            let oppMoves = getTopMoves(oppUsage);

            const myTypes = myActive ? (myActive.types || myActive.Types || myActive.type || (masterDataMap[myActive.SpeciesId || myActive.dexId]?.types) || ["NORMAL"]) : ["NORMAL"];
            const oppTypes = oppActive ? (oppActive.types || oppActive.Types || (masterDataMap[oppActive.SpeciesId || oppActive.dexId]?.types) || ["NORMAL"]) : ["NORMAL"];

            let movesToRender = isOpponentPerspective ? oppMoves : myMoves;
            let movesHtml = "";
            for (let m = 0; m < 4; m++) {
                let moveName = movesToRender[m] || "-";
                let dmgHtml = `<span class="move-dmg">0%</span>`;
                if (moveName !== "-" && masterMoveMap[moveName]) {
                    const move = masterMoveMap[moveName];
                    const oppBaseStats = oppActive ? masterDataMap[oppActive.SpeciesId || oppActive.dexId]?.baseStats : null;
                    const oppBase = oppActive ? masterDataMap[oppActive.SpeciesId || oppActive.dexId]?.baseStats : null;
                    const myBase = myActive ? masterDataMap[myActive.SpeciesId || myActive.dexId]?.baseStats : null;

                    if (isOpponentPerspective) {
                        if (oppBase && myBase) {
                            const myStats = getMyActualStats(myActive, myBase);
                            myStats.atk = applyRank(myStats.atk, myRanks.atk);
                            myStats.spa = applyRank(myStats.spa, myRanks.spa);
                            const oppStatsAtk = getOpponentStats(oppBase, true, true);
                            const oppStatsSemi = getOpponentStats(oppBase, true, false);
                            const oppStatsZero = getOpponentStats(oppBase, false, false);
                            oppStatsAtk.atk = applyRank(oppStatsAtk.atk, opponentRanks.atk);
                            oppStatsAtk.spa = applyRank(oppStatsAtk.spa, opponentRanks.spa);
                            oppStatsSemi.atk = applyRank(oppStatsSemi.atk, opponentRanks.atk);
                            oppStatsSemi.spa = applyRank(oppStatsSemi.spa, opponentRanks.spa);
                            oppStatsZero.atk = applyRank(oppStatsZero.atk, opponentRanks.atk);
                            oppStatsZero.spa = applyRank(oppStatsZero.spa, opponentRanks.spa);
                            
                            const oppItem = getOpponentItem(oppActive, opponentActiveIndex);
                            const myItem = getMyItem(myActive, myActiveIndex);
                            const oppAbility = getOpponentAbility(oppActive, opponentActiveIndex);
                            const myAbility = getMyAbility(myActive, myActiveIndex);

                            // Apply defense/sp.def ranks to myStats (defender)
                            myStats.def = applyRank(myStats.def, myRanks.def);
                            myStats.spd = applyRank(myStats.spd, myRanks.spd);

                            const dMax = calculateDamage(oppStatsAtk, myStats, move, oppTypes, myTypes, oppItem, myItem, oppAbility, myAbility, currentHpFractionOpp, currentHpFractionMy);
                            const dSemi = calculateDamage(oppStatsSemi, myStats, move, oppTypes, myTypes, oppItem, myItem, oppAbility, myAbility, currentHpFractionOpp, currentHpFractionMy);
                            const dZero = calculateDamage(oppStatsZero, myStats, move, oppTypes, myTypes, oppItem, myItem, oppAbility, myAbility, currentHpFractionOpp, currentHpFractionMy);

                            const isPhys = move.category === 'PHYSICAL';
                            const isSpec = move.category === 'SPECIAL';
                            const maxClass = isPhys ? 'phys-max' : (isSpec ? 'spec-max' : 'zero');
                            const semiClass = isPhys ? 'phys-semi' : (isSpec ? 'spec-semi' : 'zero');

                            dmgHtml = `<div class="move-dmg-boxes">
                                <span class="move-dmg-box large ${maxClass}" title="공/특공 극보정">${dMax.minRaw === 0 ? '0' : Math.floor(dMax.minScalar) + '~' + Math.floor(dMax.maxScalar)}</span>
                                <span class="move-dmg-box large ${semiClass}" title="공/특공 252보정">${dSemi.minRaw === 0 ? '0' : Math.floor(dSemi.minScalar) + '~' + Math.floor(dSemi.maxScalar)}</span>
                                <span class="move-dmg-box large zero" title="공/특공 무보정">${dZero.minRaw === 0 ? '0' : Math.floor(dZero.minScalar) + '~' + Math.floor(dZero.maxScalar)}</span>
                            </div>`;
                        }
                    } else {
                        if (myBase && oppBase) {
                            const atkStats = getMyActualStats(myActive, myBase);
                            atkStats.atk = applyRank(atkStats.atk, myRanks.atk);
                            atkStats.spa = applyRank(atkStats.spa, myRanks.spa);
                            
                            const defMax = getOpponentStats(oppBase, true, true);
                            const defSemi = getOpponentStats(oppBase, true, false);
                            const defZero = getOpponentStats(oppBase, false, false);
                            defMax.def = applyRank(defMax.def, opponentRanks.def);
                            defMax.spd = applyRank(defMax.spd, opponentRanks.spd);
                            defSemi.def = applyRank(defSemi.def, opponentRanks.def);
                            defSemi.spd = applyRank(defSemi.spd, opponentRanks.spd);
                            defZero.def = applyRank(defZero.def, opponentRanks.def);
                            defZero.spd = applyRank(defZero.spd, opponentRanks.spd);
                            
                            const myItem = getMyItem(myActive, myActiveIndex);
                            const oppItem = getOpponentItem(oppActive, opponentActiveIndex);
                            const myAbility = getMyAbility(myActive, myActiveIndex);
                            const oppAbility = getOpponentAbility(oppActive, opponentActiveIndex);

                            const dMax = calculateDamage(atkStats, defMax, move, myTypes, oppTypes, myItem, oppItem, myAbility, oppAbility, currentHpFractionMy, currentHpFractionOpp);
                            const dSemi = calculateDamage(atkStats, defSemi, move, myTypes, oppTypes, myItem, oppItem, myAbility, oppAbility, currentHpFractionMy, currentHpFractionOpp);
                            const dZero = calculateDamage(atkStats, defZero, move, myTypes, oppTypes, myItem, oppItem, myAbility, oppAbility, currentHpFractionMy, currentHpFractionOpp);

                            const isPhys = move.category === 'PHYSICAL';
                            const isSpec = move.category === 'SPECIAL';
                            const maxClass = isPhys ? 'phys-max' : (isSpec ? 'spec-max' : 'zero');
                            const semiClass = isPhys ? 'phys-semi' : (isSpec ? 'spec-semi' : 'zero');

                            dmgHtml = `<div class="move-dmg-boxes">
                                <span class="move-dmg-box large ${maxClass}" title="내구 극보정">${dMax.minRaw === 0 ? '0%' : dMax.min + '~' + dMax.max + '%'}</span>
                                <span class="move-dmg-box large ${semiClass}" title="체력만 보정">${dSemi.minRaw === 0 ? '0%' : dSemi.min + '~' + dSemi.max + '%'}</span>
                                <span class="move-dmg-box large zero" title="내구 무보정">${dZero.minRaw === 0 ? '0%' : dZero.min + '~' + dZero.max + '%'}</span>
                            </div>`;
                        }
                    }
                }

                if (isOpponentPerspective) {
                    movesHtml += `<div class="move-row">${dmgHtml}<span class="move-name" style="text-align:right;">${moveName}</span></div>`;
                } else {
                    movesHtml += `<div class="move-row"><span class="move-name">${moveName}</span>${dmgHtml}</div>`;
                }
            }

            const myItemSim = getMyItem(myActive, myActiveIndex);
            const oppItemSim = getOpponentItem(oppActive, opponentActiveIndex);
            let mySpeed = getMyActualSpeed(myActive, myItemSim);
            mySpeed = applyRank(mySpeed, myRanks.spe);

            let oppBaseSpe = oppActive && masterDataMap[oppActive.SpeciesId || oppActive.dexId] ? masterDataMap[oppActive.SpeciesId || oppActive.dexId].baseStats.spe : 0;
            let oppSpeeds = getSpeeds(oppBaseSpe, oppItemSim);
            oppSpeeds.max = applyRank(oppSpeeds.max, opponentRanks.spe);
            oppSpeeds.semi = applyRank(oppSpeeds.semi, opponentRanks.spe);
            oppSpeeds.uninvested = applyRank(oppSpeeds.uninvested, opponentRanks.spe);

            let mySideHtml = `
                <div class="sim-side">
                    ${buildStatusBoxHtml(myRanks, myStatus)}
                    <div class="sim-pokemon" style="padding:8px 10px;">
                        <img class="sprite" src="/img/pokemon/${myIcon}" onerror="this.src=''">
                        <div class="types" style="margin-bottom: 4px;">${getTypesHtml(myTypes)}</div>
                        <div class="name">${myName}</div>
                        <div class="stat-box atk" style="width:40px; text-align:center;">${mySpeed}</div>
                        <div class="hp-bar" style="background: linear-gradient(to right, #43b581 ${currentHpFractionMy * 100}%, #555 ${currentHpFractionMy * 100}%);"></div>
                        <div class="hp-text" id="myHpTextDisplay">${currentMyHpText}</div>
                    </div>
                    ${!isOpponentPerspective ? `<div class="sim-moves" style="padding:8px 6px;">${movesHtml}</div>` : ''}
                </div>
            `;

            let oppSideHtml = `
                <div class="sim-side right">
                    ${buildStatusBoxHtml(opponentRanks, opponentStatus)}
                    <div class="sim-pokemon" style="padding:8px 10px;">
                        <img class="sprite" src="/img/pokemon/${oppIcon}" onerror="this.src=''">
                        <div class="types" style="margin-bottom: 4px;">${getTypesHtml(oppTypes)}</div>
                        <div class="name">${oppName}</div>
                        <div style="display:flex; gap:4px;">
                            <div class="stat-box hp" style="text-align:center;">${oppSpeeds.max || '000'}</div>
                            <div class="stat-box atk" style="text-align:center;">${oppSpeeds.semi || '000'}</div>
                            <div class="stat-box def" style="text-align:center;">${oppSpeeds.uninvested || '000'}</div>
                        </div>
                        <div class="hp-bar" style="background: linear-gradient(to right, #43b581 ${currentHpFractionOpp * 100}%, #555 ${currentHpFractionOpp * 100}%);"></div>
                        <div class="hp-text" id="oppHpTextDisplay">${currentOpponentHpText}</div>
                    </div>
                    ${isOpponentPerspective ? `<div class="sim-moves" style="padding:8px 6px;">${movesHtml}</div>` : ''}
                </div>
            `;

            // sim-side 안에 상태박스 포함, simContent는 두 side만
            simContent.innerHTML = mySideHtml + oppSideHtml;
        }

        function renderSmallParties() {
            const myGrid = document.getElementById("myPartySmall");
            const oppGrid = document.getElementById("opponentPartySmall");
            myGrid.innerHTML = "";
            oppGrid.innerHTML = "";

            for (let i = 0; i < 6; i++) {
                if (i < myParty.length) {
                    const p = myParty[i];
                    const iconFile = String(p.SpeciesId).padStart(4, '0') + "_default.png";
                    const actClass = i === myActiveIndex ? "active" : "";
                    const name = p.name || p.NameKo || dummyNames[p.SpeciesId || p.dexId] || "포켓몬";
                    myGrid.innerHTML += `
                        <div class="small-card ${actClass}" onclick="selectMyActive(${i})">
                            <img class="sprite" src="/img/pokemon/${iconFile}" onerror="this.src='';">
                            <div class="types">${getTypesHtml(p.types || p.Types || p.type || (masterDataMap[p.SpeciesId || p.dexId]?.types) || ["NORMAL"])}</div>
                            <div class="name">${name}</div>
                        </div>`;
                } else {
                    myGrid.innerHTML += `<div class="small-card"></div>`;
                }

                if (i < opponentParty.length) {
                    const p = opponentParty[i];
                    const actClass = i === opponentActiveIndex ? "opponent-active" : "";
                    oppGrid.innerHTML += `
                        <div class="small-card ${actClass}" onclick="selectOpponentActive(${i})">
                            <img class="sprite" src="/img/pokemon/${p.iconFile}" onerror="this.src='';">
                            <div class="types">${getTypesHtml(p.types || p.Types || (masterDataMap[p.SpeciesId || p.dexId]?.types) || ["NORMAL"])}</div>
                            <div class="name">${p.name || p.NameKo || dummyNames[p.dexId || p.SpeciesId] || "상대 포켓몬"}</div>
                        </div>`;
                } else {
                    oppGrid.innerHTML += `<div class="small-card"></div>`;
                }
            }
        }

        function renderDetailPanels() {
            const myPanel = document.getElementById("myDetailPanel");
            const myActive = myParty[myActiveIndex];
            if (myActive) {
                const name = dummyNames[myActive.SpeciesId] || "포켓몬";
                
                let currentMyItem = getMyItem(myActive, myActiveIndex);
                let myItemHtml = ``;
                const commonItems = ["구애머리띠", "구애안경", "구애스카프", "생명의구슬", "돌격조끼", "기합의띠", "달인의띠", "오카열매", "자석", "신비의물방울"];
                
                if (myActive.ItemKo && !commonItems.includes(myActive.ItemKo)) {
                    myItemHtml += `<option value="${myActive.ItemKo}" ${currentMyItem === myActive.ItemKo ? "selected" : ""}>${myActive.ItemKo}</option>`;
                }
                commonItems.forEach(item => {
                    myItemHtml += `<option value="${item}" ${currentMyItem === item ? "selected" : ""}>${item}</option>`;
                });
                myItemHtml += `<option value="" ${currentMyItem === "" ? "selected" : ""}>선택 안함</option>`;

                let currentMyAbility = getMyAbility(myActive, myActiveIndex);
                let myAbilityHtml = ``;
                if (myActive.AbilityKo) {
                    myAbilityHtml += `<option value="${myActive.AbilityKo}" ${currentMyAbility === myActive.AbilityKo ? "selected" : ""}>${myActive.AbilityKo}</option>`;
                }
                myAbilityHtml += `<option value="" ${currentMyAbility === "" ? "selected" : ""}>선택 안함</option>`;

                myPanel.innerHTML = `
                    <h4>현재 내 포켓몬: ${name}</h4>
                    <div class="detail-row"><span class="label">지닌도구</span><select id="myItemSelect" onchange="userSelectedItemMy[(myParty[myActiveIndex] && myParty[myActiveIndex].id) || myActiveIndex]=this.value; renderAll();">${myItemHtml}</select></div>
                    <div class="detail-row"><span class="label">특성</span><select id="myAbilitySelect" onchange="userSelectedAbilityMy[(myParty[myActiveIndex] && myParty[myActiveIndex].id) || myActiveIndex]=this.value; renderAll();">${myAbilityHtml}</select></div>
                    <div class="detail-row"><span class="label">성격</span><select><option>${myActive.NatureKo || "알 수 없음"}</option></select></div>
                    <div class="detail-row"><span class="label">노력치</span><select><option>${formatMyEV(myActive.Evs)}</option></select></div>
                `;
            }

            const oppActive = opponentParty[opponentActiveIndex];
            if (oppActive) {
                const oppPanelName = document.querySelector("#opponentDetailPanel h4");
                if (oppPanelName) oppPanelName.textContent = `현재 상대: ${oppActive.name || oppActive.Name}`;

                const oppUsage = getUsageForDexId(oppActive.dexId || oppActive.DexId || oppActive.SpeciesId);

                const itemSelect = document.getElementById("oppItemSelect");
                const abilitySelect = document.getElementById("oppAbilitySelect");
                const natureSelect = document.getElementById("oppNatureSelect");
                const evSelect = document.getElementById("oppEvSelect");

                itemSelect.innerHTML = "";
                abilitySelect.innerHTML = "";
                natureSelect.innerHTML = "";
                evSelect.innerHTML = "";

                if (oppUsage) {
                    const currentOppItem = getOpponentItem(oppActive, opponentActiveIndex);
                    let itemHtml = ``;
                    if (oppUsage.items && oppUsage.items.length > 0) {
                        oppUsage.items.forEach(i => {
                            const isSelected = currentOppItem === i.ko ? "selected" : "";
                            itemHtml += `<option value="${i.ko}" ${isSelected}>${i.ko} 추정 ${i.pct}%</option>`;
                        });
                    }
                    itemHtml += `<option value="" ${currentOppItem === "" ? "selected" : ""}>선택 안함</option>`;
                    itemSelect.innerHTML = itemHtml;
                    
                    itemSelect.onchange = (e) => {
                        userSelectedItemOpp[(oppActive && oppActive.id) || opponentActiveIndex] = e.target.value;
                        renderAll();
                    };

                    const currentOppAbility = getOpponentAbility(oppActive, opponentActiveIndex);
                    let abilsHtml = ``;
                    if (oppUsage.abils && oppUsage.abils.length > 0) {
                        oppUsage.abils.forEach(a => {
                            abilsHtml += `<option value="${a.ko}" ${currentOppAbility === a.ko ? "selected" : ""}>${a.ko} 추정 ${a.pct}%</option>`;
                        });
                    }
                    abilsHtml += `<option value="" ${currentOppAbility === "" ? "selected" : ""}>선택 안함</option>`;
                    abilitySelect.innerHTML = abilsHtml;

                    abilitySelect.onchange = (e) => {
                        userSelectedAbilityOpp[(oppActive && oppActive.id) || opponentActiveIndex] = e.target.value;
                        renderAll();
                    };

                    if (oppUsage.natures && oppUsage.natures.length > 0) {
                        oppUsage.natures.forEach(n => natureSelect.innerHTML += `<option>${n.ko} 추정 ${n.pct}%</option>`);
                    }
                    natureSelect.innerHTML += "<option value=''>선택 안함</option>";

                    if (oppUsage.spreads && oppUsage.spreads.length > 0) {
                        oppUsage.spreads.forEach(s => {
                            const evStr = formatEV(s.ev);
                            evSelect.innerHTML += `<option>${evStr} 추정 ${s.pct}%</option>`;
                        });
                    }
                    evSelect.innerHTML += "<option value=''>선택 안함</option>";
                } else {
                    itemSelect.innerHTML = "<option value=''>선택 안함</option>";
                    abilitySelect.innerHTML = "<option value=''>선택 안함</option>";
                    natureSelect.innerHTML = "<option value=''>선택 안함</option>";
                    evSelect.innerHTML = "<option value=''>선택 안함</option>";
                }
                
                document.getElementById("myRankDisplay").textContent = formatRanks(myRanks);
                document.getElementById("oppRankDisplay").textContent = formatRanks(opponentRanks);
            }
        }

        window.selectMyActive = function (index) {
            myActiveIndex = index;
            renderAll();
        }

        window.selectOpponentActive = function (index) {
            opponentActiveIndex = index;
            renderAll();
        }

        // Initialization & Data fetching
        fetch("/api/myparty")
            .then(res => res.json())
            .then(data => {
                if (data && data.length > 0) {
                    myParty = data[0].Members;
                    renderAll();
                }
            })
            .catch(e => console.error("Failed to load my party", e));

        const connection = new signalR.HubConnectionBuilder()
            .withUrl("/pokemonHub")
            .withAutomaticReconnect()
            .build();

        connection.on("UpdateOpponentParty", function (partyData) {
            opponentParty = partyData;
            renderAll();
        });

        connection.on("UpdateHpEvent", function (ev) {
            let changed = false;
            if (ev.myHp) {
                currentMyHpText = "HP " + ev.myHp;
                // Parse "171/171" or "84/171"
                let parts = ev.myHp.split('/');
                if (parts.length === 2) {
                    let cur = parseFloat(parts[0]);
                    let max = parseFloat(parts[1]);
                    if (!isNaN(cur) && !isNaN(max) && max > 0) {
                        currentHpFractionMy = cur / max;
                        currentMyHpText = "HP " + cur + " / " + max;
                    }
                }
                changed = true;
            }
            if (ev.opponentHp) {
                currentOpponentHpText = "HP " + ev.opponentHp;
                // Parse "100%" or "84%"
                let oppStr = ev.opponentHp.replace('%', '').trim();
                let pct = parseFloat(oppStr);
                if (!isNaN(pct)) {
                    currentHpFractionOpp = pct / 100.0;
                    currentOpponentHpText = "HP " + pct + "%";
                }
                changed = true;
            }
            if (changed) renderAll();
        });

        connection.on("BattleLogEvent", function (ev) {
            console.log("BattleLogEvent received:", ev);
            if (ev.eventType === "MegaEvolution") {
                const source = ev.source;
                const form = ev.payload || "Normal";
                
                let activeP = source === "My" ? myParty[myActiveIndex] : opponentParty[opponentActiveIndex];

                if (activeP) {
                    const originalId = activeP.SpeciesId || activeP.dexId;
                    let megaFound = null;
                    for (const key in masterDataMap) {
                        const m = masterDataMap[key];
                        if (m.isMegaForm && m.megaBaseSpeciesId === originalId) {
                            if (form === "X" && m.nameKo.endsWith("X")) { megaFound = m; break; }
                            if (form === "Y" && m.nameKo.endsWith("Y")) { megaFound = m; break; }
                            if (form === "Normal" && !m.nameKo.endsWith("X") && !m.nameKo.endsWith("Y")) { megaFound = m; break; }
                        }
                    }
                    if (!megaFound) {
                        for (const key in masterDataMap) {
                            const m = masterDataMap[key];
                            if (m.isMegaForm && m.megaBaseSpeciesId === originalId) {
                                megaFound = m; break;
                            }
                        }
                    }

                    if (megaFound) {
                        let megaSuffix = (form === "X" || form === "Y") ? ("mega" + form) : "mega";
                        let newIconFile = String(originalId).padStart(4, '0') + "_" + megaSuffix + ".png";
                        
                        if (source === "My") {
                            myParty[myActiveIndex] = {
                                ...activeP,
                                SpeciesId: megaFound.id,
                                dexId: megaFound.id,
                                iconFile: newIconFile,
                                types: megaFound.types,
                                name: megaFound.nameKo,
                                NameKo: megaFound.nameKo
                            };
                        } else {
                            opponentParty[opponentActiveIndex] = {
                                ...activeP,
                                SpeciesId: megaFound.id,
                                dexId: megaFound.id,
                                iconFile: newIconFile,
                                types: megaFound.types,
                                name: megaFound.nameKo,
                                NameKo: megaFound.nameKo
                            };
                        }
                    }
                }
            } else if (ev.eventType === "RankChange" && ev.payload) {
                const stat = ev.payload.stat.toLowerCase();
                const stages = ev.payload.stages;
                if (ev.source === "My") {
                    if (myRanks[stat] !== undefined) myRanks[stat] = Math.max(-6, Math.min(6, myRanks[stat] + stages));
                } else if (ev.source === "Opponent") {
                    if (opponentRanks[stat] !== undefined) opponentRanks[stat] = Math.max(-6, Math.min(6, opponentRanks[stat] + stages));
                }
            } else if (ev.eventType === "StatusChange" && ev.payload) {
                // payload: "PRZ"|"BRN"|"PSN"|"TOX"|"SLP"|"FRZ"|null
                if (ev.source === "My") myStatus = ev.payload || null;
                else if (ev.source === "Opponent") opponentStatus = ev.payload || null;
            } else if (ev.eventType === "WeatherChange" && ev.payload) {
                currentWeather = ev.payload;
            } else if (ev.eventType === "Switch") {
                if (ev.source === "My") {
                    myRanks = { atk: 0, def: 0, spa: 0, spd: 0, spe: 0 };
                    myStatus = null;
                    if (ev.description) {
                        for (let i = 0; i < myParty.length; i++) {
                            let n = myParty[i].name || myParty[i].NameKo || dummyNames[myParty[i].SpeciesId || myParty[i].dexId];
                            if (n && ev.description.includes(n)) {
                                myActiveIndex = i;
                                break;
                            }
                        }
                    }
                } else if (ev.source === "Opponent") {
                    opponentRanks = { atk: 0, def: 0, spa: 0, spd: 0, spe: 0 };
                    opponentStatus = null;
                    if (ev.description) {
                        for (let i = 0; i < opponentParty.length; i++) {
                            let n = opponentParty[i].name || opponentParty[i].NameKo || dummyNames[opponentParty[i].dexId || opponentParty[i].SpeciesId];
                            if (n && ev.description.includes(n)) {
                                opponentActiveIndex = i;
                                break;
                            }
                        }
                    }
                }
            }
            renderAll();
        });

        connection.on("BattleReset", function () {
            myRanks = { atk: 0, def: 0, spa: 0, spd: 0, spe: 0 };
            opponentRanks = { atk: 0, def: 0, spa: 0, spd: 0, spe: 0 };
            myStatus = null;
            opponentStatus = null;
            currentWeather = null;
            opponentParty = []; // 파티 정보 초기화
            opponentActiveIndex = 0;
            renderAll();
        });

        connection.start().catch(err => console.error(err.toString()));

        window.resetBattle = function() {
            connection.invoke("ResetBattle").catch(err => console.error(err.toString()));
        };

        renderAll();
    