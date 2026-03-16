import { readFileSync, existsSync } from "fs";
import path from "path";
import { fileURLToPath } from "url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

// ─────────────────────────────────────────────────────────────
// Env loading
// ─────────────────────────────────────────────────────────────
const envPath = path.join(__dirname, "..", ".env");
if (existsSync(envPath)) {
  for (const line of readFileSync(envPath, "utf-8").split("\n")) {
    const m = line.match(/^([^#=\s][^=]*)=(.*)$/);
    if (m) process.env[m[1].trim()] ??= m[2].trim();
  }
}

const API_KEY = (
  process.env.USER_API_KEY ??
  process.env.AIDEVS_API_KEY ??
  process.env.PERSONAL_API_KEY ??
  ""
).trim();
const HUB = "https://hub.ag3nts.org";

if (!API_KEY) {
  console.error(
    "Error: No hub API key found.\n" +
    "Add your personal AI Devs course key to .env as:\n" +
    "  USER_API_KEY=your-key-here"
  );
  process.exit(1);
}

// ─────────────────────────────────────────────────────────────
// Power plant coordinates (geocoded from city names in findhim_locations.json)
// ─────────────────────────────────────────────────────────────
const POWER_PLANTS: Record<string, { lat: number; lng: number; code: string }> = {
  "Zabrze":                    { lat: 50.3254,  lng: 18.7842,  code: "PWR3847PL" },
  "Piotrków Trybunalski":    { lat: 51.4024,  lng: 19.7012,  code: "PWR5921PL" },
  "Grudziądz":               { lat: 53.4869,  lng: 18.7539,  code: "PWR7264PL" },
  "Tczew":                     { lat: 53.7763,  lng: 18.7793,  code: "PWR1593PL" },
  "Radom":                     { lat: 51.4027,  lng: 21.1473,  code: "PWR8406PL" },
  "Chelmno":                   { lat: 53.3506,  lng: 18.4258,  code: "PWR2758PL" },
  "Żarnowiec":               { lat: 54.4928,  lng: 18.1191,  code: "PWR6132PL" },
};

// ─────────────────────────────────────────────────────────────
// Haversine distance (km)
// ─────────────────────────────────────────────────────────────
function haversine(lat1: number, lng1: number, lat2: number, lng2: number): number {
  const R = 6371;
  const dLat = ((lat2 - lat1) * Math.PI) / 180;
  const dLng = ((lng2 - lng1) * Math.PI) / 180;
  const a =
    Math.sin(dLat / 2) ** 2 +
    Math.cos((lat1 * Math.PI) / 180) *
      Math.cos((lat2 * Math.PI) / 180) *
      Math.sin(dLng / 2) ** 2;
  return R * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
}

function nearestPlant(lat: number, lng: number) {
  let best = { name: "", code: "", distance: Infinity };
  for (const [name, plant] of Object.entries(POWER_PLANTS)) {
    const d = haversine(lat, lng, plant.lat, plant.lng);
    if (d < best.distance) best = { name, code: plant.code, distance: d };
  }
  return best;
}

// ─────────────────────────────────────────────────────────────
// CSV parser (handles quoted fields with embedded commas)
// ─────────────────────────────────────────────────────────────
type Person = { name: string; surname: string; birthYear: number };

function parseCsv(content: string): Person[] {
  const people: Person[] = [];
  const lines = content.split(/\r?\n/);
  for (let i = 1; i < lines.length; i++) {
    const line = lines[i].trim();
    if (!line) continue;
    const fields: string[] = [];
    let cur = "";
    let inQ = false;
    for (const ch of line) {
      if (ch === '"') { inQ = !inQ; continue; }
      if (ch === "," && !inQ) { fields.push(cur); cur = ""; continue; }
      cur += ch;
    }
    fields.push(cur);
    if (fields.length < 4) continue;
    const [name, surname, , birthDate] = fields;
    const birthYear = parseInt(birthDate.split("-")[0], 10);
    if (name && surname && !isNaN(birthYear)) people.push({ name, surname, birthYear });
  }
  return people;
}

// ─────────────────────────────────────────────────────────────
// HTTP helpers
// ─────────────────────────────────────────────────────────────
async function postJson(url: string, body: object, retries = 5): Promise<unknown> {
  for (let attempt = 0; attempt < retries; attempt++) {
    try {
      const res = await fetch(url, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
      if (!res.ok) throw new Error(`HTTP ${res.status}: ${await res.text()}`);
      const json = await res.json();
      // Rate-limit response from hub — retry without counting as a failed attempt
      if (json && typeof json === "object" && !Array.isArray(json) && (json as Record<string, unknown>).code === -9999) {
        const wait = 2000 * (attempt + 1);
        console.warn(`  [RATE LIMIT] Retrying in ${wait}ms…`);
        await sleep(wait);
        attempt--; // don't count this as an attempt
        continue;
      }
      return json;
    } catch (err) {
      if (attempt === retries - 1) throw err;
      await sleep(1000 * (attempt + 1));
    }
  }
}

type Coord = { latitude: number; longitude: number };

async function getLocations(name: string, surname: string): Promise<Coord[]> {
  try {
    const raw = await postJson(`${HUB}/api/location`, { apikey: API_KEY, name, surname });
    const data = raw as Record<string, unknown>;
    if (Array.isArray(raw)) return raw as Coord[];
    if (Array.isArray(data.locations)) return data.locations as Coord[];
    if (Array.isArray(data.data)) return data.data as Coord[];
    console.warn(`  [WARN] Unexpected location response for ${name} ${surname}:`, JSON.stringify(raw));
    return [];
  } catch (err) {
    console.warn(`  [WARN] Location fetch failed for ${name} ${surname}:`, err);
    return [];
  }
}

async function getAccessLevel(name: string, surname: string, birthYear: number): Promise<number> {
  const raw = await postJson(`${HUB}/api/accesslevel`, { apikey: API_KEY, name, surname, birthYear });
  const data = raw as Record<string, unknown>;
  return (data.accessLevel ?? data.level ?? data.access_level ?? raw) as number;
}

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

// ─────────────────────────────────────────────────────────────
// Concurrency pool
// ─────────────────────────────────────────────────────────────
// Main
// ─────────────────────────────────────────────────────────────
async function main() {
  console.log("Reading people_select.csv…");
  const csvContent = readFileSync(path.join(__dirname, "people_select.csv"), "utf-8");
  const people = parseCsv(csvContent);
  console.log(`Loaded ${people.length} people.`);

  type Candidate = { person: Person; distance: number; code: string; plantName: string };
  let best: Candidate | null = null;

  for (let i = 0; i < people.length; i++) {
    const person = people[i];
    if (i > 0) await sleep(1500);
    console.log(`[${i + 1}/${people.length}] Fetching locations for ${person.name} ${person.surname}…`);
    const locations = await getLocations(person.name, person.surname);
    console.log(`  Got ${locations.length} location(s).`);

    for (const loc of locations) {
      const lat = loc.latitude;
      const lng = loc.longitude;
      if (lat == null || lng == null) continue;

      const nearest = nearestPlant(Number(lat), Number(lng));
      console.log(`  Coords (${lat}, ${lng}) → nearest: ${nearest.name} ${nearest.distance.toFixed(2)} km`);
      if (!best || nearest.distance < best.distance) {
        best = { person, distance: nearest.distance, code: nearest.code, plantName: nearest.name };
      }
    }
  }

  if (!best) {
    console.error("No candidate found — the API returned no location data for any person.");
    process.exit(1);
  }

  console.log(
    `\nBest match: ${best.person.name} ${best.person.surname} (${best.distance.toFixed(2)} km from ${best.plantName})`
  );

  // ── Get access level ──────────────────────────────────────
  console.log("Fetching access level…");
  const accessLevel = await getAccessLevel(best.person.name, best.person.surname, best.person.birthYear);
  console.log(`Access level: ${accessLevel}`);

  // ── Submit answer ─────────────────────────────────────────
  const answer = {
    name: best.person.name,
    surname: best.person.surname,
    accessLevel,
    powerPlant: best.code,
  };

  console.log("\nSubmitting answer:", JSON.stringify(answer, null, 2));
  const verifyResult = await postJson(`${HUB}/verify`, { apikey: API_KEY, task: "findhim", answer });
  console.log("\nVerification result:", JSON.stringify(verifyResult, null, 2));
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
