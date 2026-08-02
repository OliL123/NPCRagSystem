# Locations Inventory — for the map/graph

Everything currently in `Data/NpcDataConfig/locations.json`, plus every place the NPCs/lore imply that **doesn't** exist yet. Use this to build the connection graph. (Map geography itself is yours to design.)

---

## ✅ IN THE GAME (24 locations)

### Carvallen (region: carvallen)
| id | name | who's there |
|---|---|---|
| `carvallen` | Carvallen Main Gates | hub |
| `sleeping_hound_bar` | The Sleeping Hound | Corin, Tessa |
| `sleeping_hound_kitchen` | The Kitchen | Tessa |
| `carvallen_market` | Carvallen Market | Sael; Lucen/Mira visit Sat |
| `logging_hall` | The Logging Hall | Bren; **Aeddan, Loup, Tanguy** |

### Roads / Outskirts (region: wilderness)
| id | name | who's there |
|---|---|---|
| `northern_road` | The Northern Road South | travel node |
| `antitheis_outskirts` | Antitheis — Outskirts | **gate markets** happening (Tasco/Maddoc/Lugor/Bevan); shanties; the queue. **Isaura, Guiraut, Raimon** belong here |

### Lathvel (region: lathvel)
| id | name | who's there |
|---|---|---|
| `lathvel` | Lathvel Main Gates | hub |
| `lathvel_inn` | The Big Pour | **Doran** |
| `lathvel_store` | The Lathvel General Store | **Halvern** |
| `fate_falls` | Fate Falls | **Nevin** |
| `the_astomatory` | The Astomatory (ruined temple) | **Agonferre** |

### Antitheis (region: antitheis)
| id | name | who's there |
|---|---|---|
| `antitheis_north_gate` | The North Gate *(= the NW gate for 7th Ave)* | Caradek |
| `antitheis_outer_ring` | Outer Seventh Avenue | happenings: lessons (Sagart/Peck), **hiring crowd** (Keld → Raimon), soup line (Sagart); **Coll, Gethin, Cadwal, Brannoth, Telvric, Keld, Emrek, Brek, Leni, Solem** all Outer Ring |
| `mid_city_plaza` | Seventh Square | central hub |
| `worn_lintel` | The Worn Lintel | Vercinna |
| `tailors_bridge` | Tailor's Bridge | **Kira** |
| `bluebells_garden` | Bluebells | Dagovir (band) |
| `dallec_townhouse` | The Dallec Townhouse | **Sorvel, Lirien, Aria, Wynn** |
| `piping_corps` | Imperial Piping Corps | **Sorvel** (work) |
| `auction_house` | Brissalby's | **Lirien, Folco** |
| `mid_city_school` | Vernal Street High School | **Leni** (Briganar seal above door) |
| `antitheis_inner_ward` | Inner Seventh Avenue | the House compounds behind walls; **Corvane, Halden, Rulvra, Rethiv, Celdal, Gorrax, Mirvosa, Bix, Telvova, Veldael, Zurvael** |

---

## ❌ MISSING — implied but no location exists

### A. Real gaps worth adding (someone genuinely lives/works here)
| suggested | who needs it | notes |
|---|---|---|
| **The Collegium** (grand hall) | **Modestus** + the Astrologer's Clock; Esunoval's past; Wynn's goal; Mira's correspondent | ⭐ biggest gap. Mid City or Inner-Ward edge. Houses the great clock (`Astrologers_Clock.md`) |
| **The tannery** | **Bram** | city edge, downwind / by water |
| **The dye-works** | **Coll** | on/off the Seventh (the avenue already "reeks of the dye-works") |
| **A library** | **Solem** (writer), **Leni** (study), Krad-adjacent | Mid City |
| **The Undermarket** | **Brek** | Pre-Fall tunnels *under* the city (Bluebells sits on Pre-Fall stone; tunnels below). Already a lore name |
| **The Varenne farm** | **Lucen, Mira** | Carvallen outskirts ⚠ (they're *filed* under mid_city but live near Carvallen) |
| **Noble compounds** (split the Inner Ward) | Corruthis (Rethiv/Celdal/**Rulvra**), Ambarris (**Gorrax** + barracks), Bituris (Veldael), Catubrix (Telvova), Briganar (records) | right now the whole Inner Ward is one node |
| **A noble-house kitchen + gate** | **Corvane** (sous-chef), **Halden** (compound gate guard) | which house is theirs is open — could reuse one above or a new minor house |

### B. Probably covered by an existing node — just WIRE (don't create)
- **Raimon's dawn hiring yard** = the existing `avenue_hiring_crowd` happening at `antitheis_outer_ring` (Keld's already in it). Add Raimon.
- **Isaura's shanty camp / the wall** = `antitheis_outskirts` (shanties are in its flavour text).
- **Guiraut's complaints booth** = a booth at `antitheis_north_gate` / `antitheis_outskirts`.
- **Gate markets** = `antitheis_outskirts` happening (exists — ⚠ **Drust is missing** from its `npc_ids`: only tasco/maddoc/lugor/bevan listed).
- **Brannoth's stall** = `antitheis_outer_ring` food-stall strip (or a small outer-ring market sub-node).

### C. Referenced / offscreen — likely NO node needed (mention only)
- **The Lows** (where Sorvel & Lirien met; nightlife district; lore name)
- **The grandparents' farmstead**, eastern interior (Sorvel's parents; Wynn visits)
- **A culinary school**, Mid City (Emrek's aspiration — can't afford; may stay a dream)
- **Emrek's restaurant** / **Solem's butcher's factory** (abstract Outer/Mid workplaces)
- **The ruin garden east of the third arcade** (Lirien's Sundays — maybe = Bluebells, or a 2nd ruin garden)
- **The rooftops** — a traversal *layer* over the wards, not one node (Peck outer / Aria mid / Bix inner)

---

## Geography notes (from the flavour text, for your graph)
- **8 numbered Avenues** spoke the rings, numbered clockwise from north. The **7th Avenue = the north-west spoke** (Outer Seventh → Seventh Square → Inner Seventh), served by the **north-west gate**. Only the 7th is built out so far; the other seven avenues are implied.
- **Four squares** where the odd avenues cross the Mid City (Seventh Square is one).
- The **Onnoris** runs below; **a ravine** splits the Mid City (Tailor's Bridge spans it; Bluebells sits on its edge).
- Existing data regions: `carvallen · lathvel · wilderness · antitheis`.
