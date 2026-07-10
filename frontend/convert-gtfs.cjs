/**
 * convert-gtfs.js
 * Reads GTFS CSV files from gtfs_rapid_rail_kl/ and generates
 * two GeoJSON files for the Leaflet frontend:
 *   - public/data/kl_rail_lines.json  (LineStrings)
 *   - public/data/kl_rail_stops.json  (Points)
 *
 * Usage:  node convert-gtfs.js
 */

const fs = require('fs');
const path = require('path');
const csv = require('csv-parser');

// ---- Configuration ----
const GTFS_DIR = path.resolve(__dirname, '..', '..', 'gtfs_rapid_rail_kl');
const OUTPUT_DIR = path.resolve(__dirname, 'public', 'data');

// Helper: read a CSV file and return all rows as an array of objects
function readCsv(filePath) {
  return new Promise((resolve, reject) => {
    const rows = [];
    fs.createReadStream(filePath, { encoding: 'utf-8' })
      .pipe(csv())
      .on('data', (row) => rows.push(row))
      .on('end', () => resolve(rows))
      .on('error', (err) => reject(err));
  });
}

async function main() {
  console.log('🚉 Reading GTFS files from:', GTFS_DIR);

  // 1. Parse routes.txt → { route_id → { route_short_name, route_color } }
  console.log('   📋 routes.txt...');
  const routesRaw = await readCsv(path.join(GTFS_DIR, 'routes.txt'));
  const routeMap = {};
  for (const r of routesRaw) {
    routeMap[r.route_id] = {
      route_name: r.route_short_name || r.route_long_name || r.route_id,
      route_color: r.route_color || '3388ff',
    };
  }

  // 2. Parse trips.txt → deduplicated set of { route_id ↔ shape_id }
  console.log('   🚌 trips.txt...');
  const tripsRaw = await readCsv(path.join(GTFS_DIR, 'trips.txt'));
  const routeShapePairs = new Set();
  const routeToShapes = {}; // route_id → [shape_id, ...]
  for (const t of tripsRaw) {
    const key = `${t.route_id}|||${t.shape_id}`;
    if (!routeShapePairs.has(key)) {
      routeShapePairs.add(key);
      if (!routeToShapes[t.route_id]) routeToShapes[t.route_id] = [];
      routeToShapes[t.route_id].push(t.shape_id);
    }
  }

  // 3. Parse shapes.txt → { shape_id → [[lon, lat], ...] } sorted by sequence
  console.log('   🗺️  shapes.txt...');
  const shapesRaw = await readCsv(path.join(GTFS_DIR, 'shapes.txt'));
  const shapeCoords = {};
  for (const s of shapesRaw) {
    if (!shapeCoords[s.shape_id]) shapeCoords[s.shape_id] = [];
    shapeCoords[s.shape_id].push({
      seq: parseInt(s.shape_pt_sequence, 10),
      lon: parseFloat(s.shape_pt_lon),
      lat: parseFloat(s.shape_pt_lat),
    });
  }
  // Sort each shape by sequence number and extract [lon, lat] arrays
  for (const [sid, points] of Object.entries(shapeCoords)) {
    points.sort((a, b) => a.seq - b.seq);
    shapeCoords[sid] = points.map((p) => [p.lon, p.lat]);
  }

  // 4. Build kl_rail_lines.json (GeoJSON FeatureCollection of LineStrings)
  console.log('   🔧 Building kl_rail_lines.json...');
  const lineFeatures = [];
  for (const [routeId, shapeIds] of Object.entries(routeToShapes)) {
    const meta = routeMap[routeId];
    if (!meta) {
      console.warn(`   ⚠️  No route info for route_id="${routeId}", skipping`);
      continue;
    }
    for (const shapeId of shapeIds) {
      const coords = shapeCoords[shapeId];
      if (!coords || coords.length < 2) {
        console.warn(`   ⚠️  No/incomplete shape for shape_id="${shapeId}", skipping`);
        continue;
      }
      lineFeatures.push({
        type: 'Feature',
        properties: {
          route_id: routeId,
          route_name: meta.route_name,
          route_color: '#' + meta.route_color,
        },
        geometry: {
          type: 'LineString',
          coordinates: coords,
        },
      });
    }
  }
  const railLines = {
    type: 'FeatureCollection',
    features: lineFeatures,
  };
  const linesPath = path.join(OUTPUT_DIR, 'kl_rail_lines.json');
  fs.writeFileSync(linesPath, JSON.stringify(railLines));
  console.log(`   ✅ Wrote ${lineFeatures.length} line features → ${linesPath}`);

  // 5. Parse stops.txt → GeoJSON Point features
  console.log('   🚏 stops.txt...');
  const stopsRaw = await readCsv(path.join(GTFS_DIR, 'stops.txt'));
  const stopFeatures = [];
  for (const s of stopsRaw) {
    // Filter by location_type if the field exists (0 or 1 = stop/station)
    if ('location_type' in s) {
      const locType = parseInt(s.location_type, 10);
      if (locType !== 0 && locType !== 1) continue;
    }
    const lat = parseFloat(s.stop_lat);
    const lon = parseFloat(s.stop_lon);
    if (isNaN(lat) || isNaN(lon)) continue;
    stopFeatures.push({
      type: 'Feature',
      properties: {
        stop_id: s.stop_id,
        stop_name: s.stop_name,
      },
      geometry: {
        type: 'Point',
        coordinates: [lon, lat],
      },
    });
  }
  const railStops = {
    type: 'FeatureCollection',
    features: stopFeatures,
  };
  const stopsPath = path.join(OUTPUT_DIR, 'kl_rail_stops.json');
  fs.writeFileSync(stopsPath, JSON.stringify(railStops));
  console.log(`   ✅ Wrote ${stopFeatures.length} stop features → ${stopsPath}`);

  console.log('\n🎉 Done! GeoJSON files ready in public/data/');
}

main().catch((err) => {
  console.error('❌ Error:', err);
  process.exit(1);
});
