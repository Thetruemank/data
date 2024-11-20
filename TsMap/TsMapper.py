import os
import json
import math
from collections import defaultdict

class TsMapper:
    def __init__(self, game_dir, mods):
        self.game_dir = game_dir
        self.mods = mods
        self.is_ets2 = True
        self.sector_files = []
        self.overlay_manager = MapOverlayManager()
        self.localization = LocalizationManager()
        self.prefab_lookup = {}
        self.cities_lookup = {}
        self.countries_lookup = {}
        self.road_lookup = {}
        self.ferry_connection_lookup = []
        self.roads = []
        self.prefabs = []
        self.map_areas = []
        self.cities = []
        self.ferry_connections = []
        self.items = {}
        self.nodes = {}
        self.min_x = float('inf')
        self.max_x = float('-inf')
        self.min_z = float('inf')
        self.max_z = float('-inf')
        self.map_items = []
        self.route_roads = []
        self.route_prefabs = []
        self.route_ferry_ports = {}
        self.prefab_nav = {}
        self.ferry_port_by_id = {}

    def parse_city_files(self):
        def_directory = UberFileSystem.instance().get_directory("def")
        if def_directory is None:
            print("Could not read 'def' dir")
            return

        for city_file_name in def_directory.get_files("city"):
            city_file = UberFileSystem.instance().get_file(f"def/{city_file_name}")
            data = city_file.entry.read()
            lines = data.decode('utf-8').split('\n')
            for line in lines:
                if line.strip().startswith("#"):
                    continue
                if "@include" in line:
                    path = PathHelper.get_file_path(line.split('"')[1], "def")
                    city = TsCity(path)
                    if city.token != 0 and city.token not in self.cities_lookup:
                        self.cities_lookup[city.token] = city

    def parse_country_files(self):
        def_directory = UberFileSystem.instance().get_directory("def")
        if def_directory is None:
            print("Could not read 'def' dir")
            return

        for country_file_path in def_directory.get_files("country"):
            country_file = UberFileSystem.instance().get_file(f"def/{country_file_path}")
            data = country_file.entry.read()
            lines = data.decode('utf-8').split('\n')
            for line in lines:
                if line.strip().startswith("#"):
                    continue
                if "@include" in line:
                    path = PathHelper.get_file_path(line.split('"')[1], "def")
                    country = TsCountry(path)
                    if country.token != 0 and country.token not in self.countries_lookup:
                        self.countries_lookup[country.token] = country

    def parse_prefab_files(self):
        world_directory = UberFileSystem.instance().get_directory("def/world")
        if world_directory is None:
            print("Could not read 'def/world' dir")
            return

        for prefab_file_name in world_directory.get_files("prefab"):
            if not prefab_file_name.startswith("prefab"):
                continue
            prefab_file = UberFileSystem.instance().get_file(f"def/world/{prefab_file_name}")
            data = prefab_file.entry.read()
            lines = data.decode('utf-8').split('\n')

            token = 0
            path = ""
            category = ""
            for line in lines:
                valid_line, key, value = SiiHelper.parse_line(line)
                if valid_line:
                    if key == "prefab_model":
                        token = ScsToken.string_to_token(SiiHelper.trim(value.split('.')[1]))
                    elif key == "prefab_desc":
                        path = PathHelper.ensure_local_path(value.split('"')[1])
                    elif key == "category":
                        category = value.split('"')[1] if '"' in value else value.strip()

                if "}" in line and token != 0 and path != "":
                    prefab = TsPrefab(path, token, category)
                    if prefab.token != 0 and prefab.token not in self.prefab_lookup:
                        self.prefab_lookup[prefab.token] = prefab

                    token = 0
                    path = ""
                    category = ""

    def parse_road_look_files(self):
        world_directory = UberFileSystem.instance().get_directory("def/world")
        if world_directory is None:
            print("Could not read 'def/world' dir")
            return

        for road_look_file_name in world_directory.get_files("road_look"):
            if not road_look_file_name.startswith("road"):
                continue
            road_look_file = UberFileSystem.instance().get_file(f"def/world/{road_look_file_name}")
            data = road_look_file.entry.read()
            lines = data.decode('utf-8').split('\n')
            road_look = None
            had_offset = False
            for line in lines:
                valid_line, key, value = SiiHelper.parse_line(line)
                if valid_line:
                    if key == "road_look":
                        road_look = TsRoadLook(ScsToken.string_to_token(SiiHelper.trim(value.split('.')[1].strip('{'))))
                    if road_look is None:
                        continue
                    if key == "name":
                        road_look.name = value
                    if key == "lanes_left[]":
                        road_look.lanes_left.append(value)
                        road_look.is_local = value in ["traffic_lane.road.local", "traffic_lane.road.local.tram", "traffic_lane.road.local.no_overtake"]
                        road_look.is_express = value in ["traffic_lane.road.expressway", "traffic_lane.road.divided"]
                        road_look.is_highway = value in ["traffic_lane.road.motorway", "traffic_lane.road.motorway.low_density", "traffic_lane.road.freeway", "traffic_lane.road.freeway.low_density", "traffic_lane.road.divided"]
                        road_look.is_no_vehicles = value == "traffic_lane.no_vehicles"
                    elif key == "lanes_right[]":
                        road_look.lanes_right.append(value)
                        road_look.is_local = value in ["traffic_lane.road.local", "traffic_lane.road.local.tram", "traffic_lane.road.local.no_overtake"]
                        road_look.is_express = value in ["traffic_lane.road.expressway", "traffic_lane.road.divided"]
                        road_look.is_highway = value in ["traffic_lane.road.motorway", "traffic_lane.road.motorway.low_density", "traffic_lane.road.freeway", "traffic_lane.road.freeway.low_density", "traffic_lane.road.divided"]
                        road_look.is_no_vehicles = value == "traffic_lane.no_vehicles"
                    elif key == "road_offset":
                        road_look.offset = float(value)
                        had_offset = True
                    elif key == "shoulder_space_left":
                        road_look.shoulder_space_left = float(value)
                    elif key == "shoulder_size_left":
                        road_look.shoulder_size_left = float(value)
                    elif key == "shoulder_space_right":
                        road_look.shoulder_space_right = float(value)
                    elif key == "shoulder_size_right":
                        road_look.shoulder_size_right = float(value)
                    elif key == "lane_offsets_left":
                        road_look.lane_offsets_left.append(value)
                    elif key == "lane_offsets_right":
                        road_look.lane_offsets_right.append(value)
                    elif key == "road_size_left":
                        road_look.road_size_left = float(value)
                    elif key == "road_size_right":
                        road_look.road_size_right = float(value)

                if "}" in line and road_look is not None:
                    if not had_offset:
                        road_look.offset = 999
                    if road_look.token != 0 and road_look.token not in self.road_lookup:
                        self.road_lookup[road_look.token] = road_look
                        road_look = None

    def parse_ferry_connections(self):
        connection_directory = UberFileSystem.instance().get_directory("def/ferry/connection")
        if connection_directory is None:
            print("Could not read 'def/ferry/connection' dir")
            return

        for ferry_connection_file_path in connection_directory.get_files_by_extension("def/ferry/connection", [".sui", ".sii"]):
            ferry_connection_file = UberFileSystem.instance().get_file(ferry_connection_file_path)
            data = ferry_connection_file.entry.read()
            lines = data.decode('utf-8').split('\n')

            conn = None
            start_port_token = 0
            end_port_token = 0
            price = 0
            time = 0
            distance = 0

            for line in lines:
                valid_line, key, value = SiiHelper.parse_line(line)
                if valid_line:
                    if conn is not None:
                        if "connection_positions" in key:
                            index = int(key.split('[')[1].split(']')[0])
                            vector = value.split('(')[1].split(')')[0]
                            values = vector.split(',')
                            x = float(values[0])
                            z = float(values[2])
                            conn.add_connection_position(index, x, z)
                        elif "connection_directions" in key:
                            index = int(key.split('[')[1].split(']')[0])
                            vector = value.split('(')[1].split(')')[0]
                            values = vector.split(',')
                            x = float(values[0])
                            z = float(values[2])
                            conn.add_rotation(index, math.atan2(z, x))

                    if key == "ferry_connection":
                        port_ids = value.split('.')
                        start_port_token = ScsToken.string_to_token(port_ids[1])
                        end_port_token = ScsToken.string_to_token(port_ids[2].strip('{').strip())
                        conn = TsFerryConnection(start_port_token, end_port_token, price, time, distance)

                    if "price" in key:
                        try:
                            price = int(value)
                        except:
                            price = 0

                    if "time" in key:
                        time = int(value)

                    if "distance" in key:
                        distance = int(value)

                if "}" in line and conn is not None:
                    old_conn = conn
                    conn = TsFerryConnection(start_port_token, end_port_token, price, time, distance)
                    if old_conn is not None:
                        for i in old_conn.connections:
                            conn.connections.append(i)

                    existing_item = next((item for item in self.ferry_connection_lookup if (item.start_port_token == conn.start_port_token and item.end_port_token == conn.end_port_token) or (item.start_port_token == conn.end_port_token and item.end_port_token == conn.start_port_token)), None)
                    if existing_item is None:
                        self.ferry_connection_lookup.append(conn)

    def parse_def_files(self):
        start_time = time.time()
        self.parse_city_files()
        print(f"Loaded {len(self.cities_lookup)} cities in {time.time() - start_time:.2f}ms")

        start_time = time.time()
        self.parse_country_files()
        print(f"Loaded {len(self.countries_lookup)} countries in {time.time() - start_time:.2f}ms")

        start_time = time.time()
        self.parse_prefab_files()
        print(f"Loaded {len(self.prefab_lookup)} prefabs in {time.time() - start_time:.2f}ms")

        start_time = time.time()
        self.parse_road_look_files()
        print(f"Loaded {len(self.road_lookup)} roads in {time.time() - start_time:.2f}ms")

        start_time = time.time()
        self.parse_ferry_connections()
        print(f"Loaded {len(self.ferry_connection_lookup)} ferry connections in {time.time() - start_time:.2f}ms")

    def load_sector_files(self):
        base_map_entry = UberFileSystem.instance().get_directory("map")
        if base_map_entry is None:
            print("Could not read 'map' dir")
            return

        mbd_file_paths = base_map_entry.get_files_by_extension("map", ".mbd")
        if not mbd_file_paths:
            print("Could not find mbd file")
            return

        self.sector_files = []

        for file_path in mbd_file_paths:
            map_name = PathHelper.get_file_name_without_extension_from_path(file_path)
            self.is_ets2 = not (map_name == "usa")

            map_file_dir = UberFileSystem.instance().get_directory(f"map/{map_name}")
            if map_file_dir is None:
                print(f"Could not read 'map/{map_name}' directory")
                return

            self.sector_files.extend(map_file_dir.get_files_by_extension(f"map/{map_name}", ".base"))

    def load_navigation(self):
        print(f"There are {len(self.prefabs)} prefabs")
        for prefab in self.prefabs:
            for node_str in prefab.nodes:
                node = self.get_node_by_uid(node_str)
                road = None
                precnode = node
                precitem = prefab
                nextnode = None
                nextitem = None
                roads = []
                total_length = 0.0
                if node.forward_item is not None and node.forward_item.type == TsItemType.Road:
                    road = node.forward_item
                elif node.backward_item is not None and node.backward_item.type == TsItemType.Road:
                    road = node.backward_item
                if road is not None:
                    direction = 0
                    if road.end_node_uid == node.uid:
                        direction = 1
                    while road is not None and road.type != TsItemType.Prefab and not road.hidden:
                        length = math.sqrt((self.get_node_by_uid(road.start_node_uid).x - self.get_node_by_uid(road.end_node_uid).x) ** 2 + (self.get_node_by_uid(road.start_node_uid).z - self.get_node_by_uid(road.end_node_uid).z) ** 2)
                        road_obj = road
                        total_length += length / road_obj.road_look.get_width()
                        roads.append(road)
                        if self.get_node_by_uid(road.start_node_uid) == precnode:
                            nextnode = self.get_node_by_uid(road.end_node_uid)
                            precnode = self.get_node_by_uid(road.end_node_uid)
                        else:
                            nextnode = self.get_node_by_uid(road.start_node_uid)
                            precnode = self.get_node_by_uid(road.start_node_uid)
                        if nextnode.backward_item == road or nextnode.backward_item == precitem:
                            nextitem = nextnode.forward_item
                            precitem = nextnode.forward_item
                        else:
                            nextitem = nextnode.backward_item
                            precitem = nextnode.backward_item
                        road = nextitem
                    if road is not None and not road.hidden:
                        prev_prefab = prefab
                        next_prefab = road
                        look = roads[-1].road_look
                        if prev_prefab.hidden or next_prefab.hidden:
                            continue
                        if next_prefab not in prev_prefab.navigation and (look.is_bidirectional() or direction == 0):
                            prev_prefab.navigation[next_prefab] = (total_length, roads)
                        if prev_prefab not in next_prefab.navigation and (look.is_bidirectional() or direction == 1):
                            reverse = list(reversed(roads))
                            next_prefab.navigation[prev_prefab] = (total_length, reverse)
                elif node.forward_item is not None and node.backward_item is not None:
                    forward_prefab = node.forward_item
                    backward_prefab = node.backward_item
                    if forward_prefab.hidden or backward_prefab.hidden:
                        continue
                    if backward_prefab not in forward_prefab.navigation:
                        forward_prefab.navigation[backward_prefab] = (0, None)
                    if forward_prefab not in backward_prefab.navigation:
                        backward_prefab.navigation[forward_prefab] = (0, None)

        ferry_to_prefab = {}
        for port in self.ferry_connections:
            min_distance = float('inf')
            closer_prefab = None
            for prefab in self.prefabs:
                distance = math.sqrt((port.x - prefab.x) ** 2 + (port.z - prefab.z) ** 2)
                if distance < min_distance and len(prefab.navigation) > 1 and not prefab.hidden:
                    min_distance = distance
                    closer_prefab = prefab
            ferry_to_prefab[port.ferry_port_id] = closer_prefab
        for port in self.ferry_connections:
            for connection in self.lookup_ferry_connection(port.ferry_port_id):
                ports = [self.ferry_port_by_id[connection.start_port_token], self.ferry_port_by_id[connection.end_port_token]]
                ferry_to_prefab[connection.start_port_token].navigation[ferry_to_prefab[connection.end_port_token]] = (connection.distance, ports)
                ports.reverse()
                ferry_to_prefab[connection.end_port_token].navigation[ferry_to_prefab[connection.start_port_token]] = (connection.distance, ports)

    def calculate_path(self, start, end):
        nodes_to_walk = {node: (float('inf'), None) for node in self.prefabs}
        walked_nodes = {}

        if start not in nodes_to_walk or end not in nodes_to_walk:
            return

        nodes_to_walk[start] = (0, None)

        while len(walked_nodes) != len(nodes_to_walk):
            distance_walked = float('inf')
            to_walk = None
            for node, (d_tmp, _) in nodes_to_walk.items():
                if distance_walked > d_tmp:
                    distance_walked = d_tmp
                    to_walk = node
            if to_walk is None:
                break

            walked_nodes[to_walk] = nodes_to_walk[to_walk]
            del nodes_to_walk[to_walk]

            if to_walk.uid == end.uid:
                break

            current_weight = walked_nodes[to_walk][0]

            for jump, (jump_distance, _) in to_walk.navigation.items():
                new_weight = jump_distance + current_weight
                new_node = jump

                if walked_nodes[to_walk][1] is not None:
                    prec_prefab = walked_nodes[to_walk][1]
                    middle_prefab = to_walk
                    prec_road = None
                    while prec_road is None and prec_prefab is not None:
                        prec_road = prec_prefab.navigation[middle_prefab][1]
                        middle_prefab = prec_prefab
                        prec_prefab = walked_nodes[prec_prefab][1]
                    next_road = to_walk.navigation[new_node][1]
                    if prec_road is not None and next_road is not None and len(prec_road) != 0 and len(next_road) != 0 and isinstance(prec_road[-1], TsRoadItem) and isinstance(next_road[0], TsRoadItem):
                        result = self.set_internal_route_prefab(prec_road[-1], next_road[0])
                        if not result[0]:
                            continue
                        else:
                            new_weight += result[1]

                if new_node not in walked_nodes and nodes_to_walk[new_node][0] > new_weight:
                    nodes_to_walk[new_node] = (new_weight, to_walk)

        route = end
        while route is not None:
            goto_new = walked_nodes.get(route, nodes_to_walk.get(route))[1]
            if goto_new is None:
                break
            if goto_new in goto_new.navigation and goto_new.navigation[route][1] is not None:
                if len(goto_new.navigation[route][1]) == 2 and isinstance(goto_new.navigation[route][1][0], TsFerryItem) and isinstance(goto_new.navigation[route][1][1], TsFerryItem):
                    start_port = goto_new.navigation[route][1][0]
                    end_port = goto_new.navigation[route][1][1]
                    if start_port not in self.route_ferry_ports:
                        self.route_ferry_ports[start_port] = end_port
                else:
                    for i in range(len(goto_new.navigation[route][1]) - 1, -1, -1):
                        self.route_roads.append(goto_new.navigation[route][1][i])
            route = goto_new

        self.route_roads.reverse()

    def calculate_prefabs_path(self):
        self.route_prefabs.clear()
        self.prefab_nav.clear()
        for i in range(len(self.route_roads) - 1):
            self.set_internal_route_prefab(self.route_roads[i], self.route_roads[i + 1])

    def set_items(self):
        for item in self.roads:
            self.items[item.uid] = item
        for item in self.prefabs:
            self.items[item.uid] = item
        for item in self.cities:
            self.items[item.uid] = item
        for item in self.ferry_connections:
            try:
                self.items[item.uid] = item
            except:
                pass

    def set_forward_backward(self):
        for node in self.nodes.values():
            item = self.items.get(node.forward_item_uid)
            if item:
                node.forward_item = item
            item = self.items.get(node.backward_item_uid)
            if item:
                node.backward_item = item

    def set_internal_route_prefab(self, start, end):
        start_node = None
        visited = {}
        prefabs_to_check = []
        possible_paths = []
        if start.start_node_uid in self.nodes and (self.nodes[start.start_node_uid].backward_item.type == TsItemType.Prefab or self.nodes[start.start_node_uid].forward_item.type == TsItemType.Prefab):
            start_node = self.nodes[start.start_node_uid]
            prefab = start_node.backward_item if start_node.backward_item.type == TsItemType.Prefab else start_node.forward_item
            temp = [(start_node, prefab)]
            prefabs_to_check.append(temp)
        if start.end_node_uid in self.nodes and (self.nodes[start.end_node_uid].backward_item.type == TsItemType.Prefab or self.nodes[start.end_node_uid].forward_item.type == TsItemType.Prefab):
            start_node = self.nodes[start.end_node_uid]
            prefab = start_node.backward_item if start_node.backward_item.type == TsItemType.Prefab else start_node.forward_item
            temp = [(start_node, prefab)]
            prefabs_to_check.append(temp)
        while prefabs_to_check:
            actual_path = prefabs_to_check.pop()
            actual_prefab = actual_path[-1]

            if actual_prefab[1] in visited:
                continue
            visited[actual_prefab[1]] = True

            last_node = actual_prefab[1].node_item_in_prefab(self, end)
            if last_node:
                actual_path.append((last_node, None))
                possible_paths.append(actual_path)
                continue

            for prefab in actual_prefab[1].node_prefab_in_prefab(self):
                new_path = list(actual_path)
                new_path.append(prefab)
                prefabs_to_check.append(new_path)

        return_value = (False, 0)
        for path in possible_paths:
            success = True
            total_length = 0.0
            for i in range(len(path) - 1):
                temp_data = self.add_prefab_path(path[i][1], path[i][0], path[i + 1][0])
                if not temp_data[0]:
                    success = False
                    break
                total_length += temp_data[1]
            if success and len(path) >= 1:
                return (True, total_length / start.road_look.get_width())
        return return_value

    def add_prefab_path(self, prefab, start_node, end_node):
        return_value = (False, 0)
        s = prefab.get_nearest_node(self, start_node, 0)
        e = prefab.get_nearest_node(self, end_node, 1)
        if s.id == -1 or e.id == -1:
            return return_value
        key = (s, e)
        if key in prefab.prefab.navigation_routes:
            self.prefab_nav[prefab] = prefab.prefab.navigation_routes[key][0]
            return_value = (True, prefab.prefab.navigation_routes[key][1])
        return return_value

    def parse(self):
        start_time = time.time()

        if not os.path.exists(self.game_dir):
            print("Could not find Game directory.")
            return

        UberFileSystem.instance().add_source_directory(self.game_dir)

        self.mods.reverse()

        for mod in self.mods:
            if mod.load:
                UberFileSystem.instance().add_source_file(mod.mod_path)

        UberFileSystem.instance().add_source_file(os.path.join(os.getcwd(), "custom_resources.zip"))

        print(f"Loaded all .scs files in {time.time() - start_time:.2f}ms")

        self.parse_def_files()
        self.load_sector_files()

        pre_locale_time = time.time()
        self.localization.load_locale_values()
        print(f"It took {time.time() - pre_locale_time:.2f} ms to read all locale files")

        if not self.sector_files:
            return
        pre_map_parse_time = time.time()
        self.sectors = [TsSector(self, file) for file in self.sector_files]
        for sec in self.sectors:
            sec.parse()
        for sec in self.sectors:
            sec.clear_file_data()
        self.set_items()
        self.set_forward_backward()
        print(f"It took {time.time() - pre_map_parse_time:.2f} ms to parse all (*.base) files")

        for map_item in self.map_items:
            map_item.update()

        invalid_ferry_connections = [x for x in self.ferry_connection_lookup if x.start_port_location == (0, 0) or x.end_port_location == (0, 0)]
        for invalid_ferry_connection in invalid_ferry_connections:
            self.ferry_connection_lookup.remove(invalid_ferry_connection)
            print(f"Ignored ferry connection '{ScsToken.token_to_string(invalid_ferry_connection.start_port_token)}-{ScsToken.token_to_string(invalid_ferry_connection.end_port_token)}' due to not having Start/End location set.")

        print(f"Loaded {self.overlay_manager.get_overlay_images_count()} overlay images, with {len(self.overlay_manager.get_overlays())} overlays on the map")

        print("Loading navigation data...")
        self.load_navigation()

        print("Starting Calculating Path...")

        start_coord = (-10109.01, 45631.55)
        end_coord = (-8042.469, -44051.7)

        first_pf_itm = None
        for i in self.prefabs:
            x = i.x - start_coord[0]
            z = i.z - start_coord[1]
            if -1 <= x <= 1 and -1 <= z <= 1:
                first_pf_itm = i
                break

        second_pf_itm = None
        for i in self.prefabs:
            x = i.x - end_coord[0]
            z = i.z - end_coord[1]
            if -10 <= x <= 10 and -10 <= z <= 10:
                second_pf_itm = i
                break

        self.calculate_path(first_pf_itm, second_pf_itm)

    def export_info(self, export_flags, export_path):
        if export_flags.is_active(ExportFlags.CityList):
            self.export_cities(export_flags, export_path)
        if export_flags.is_active(ExportFlags.CountryList):
            self.export_countries(export_flags, export_path)
        if export_flags.is_active(ExportFlags.OverlayList):
            self.export_overlays(export_flags, export_path)
        Exporter.export(self)

    def export_cities(self, export_flags, path):
        if not os.path.exists(path):
            return
        cities_jarr = []
        for city in self.cities:
            if city.hidden:
                continue
            city_jobj = city.city.to_dict()
            city_jobj["X"] = city.x
            city_jobj["Y"] = city.z
            if ScsToken.string_to_token(city.city.country) in self.countries_lookup:
                country = self.countries_lookup[ScsToken.string_to_token(city.city.country)]
                city_jobj["CountryId"] = country.country_id
            else:
                print(f"Could not find country for {city.city.name}")

            if export_flags.is_active(ExportFlags.CityLocalizedNames):
                city_jobj["LocalizedNames"] = {}
                for locale in self.localization.get_locales():
                    loc_city_name = self.localization.get_locale_value(city.city.localization_token, locale)
                    if loc_city_name is not None:
                        city_jobj["LocalizedNames"][locale] = loc_city_name

            cities_jarr.append(city_jobj)
        with open(os.path.join(path, "Cities.json"), "w") as f:
            json.dump(cities_jarr, f, indent=4)

    def export_countries(self, export_flags, path):
        if not os.path.exists(path):
            return
        countries_jarr = []
        for country in self.countries_lookup.values():
            country_jobj = country.to_dict()
            if export_flags.is_active(ExportFlags.CountryLocalizedNames):
                country_jobj["LocalizedNames"] = {}
                for locale in self.localization.get_locales():
                    loc_country_name = self.localization.get_locale_value(country.localization_token, locale)
                    if loc_country_name is not None:
                        country_jobj["LocalizedNames"][locale] = loc_country_name
            countries_jarr.append(country_jobj)
        with open(os.path.join(path, "Countries.json"), "w") as f:
            json.dump(countries_jarr, f, indent=4)

    def export_overlays(self, export_flags, path):
        if not os.path.exists(path):
            return

        save_as_png = export_flags.is_active(ExportFlags.OverlayPNGs)

        overlay_path = os.path.join(path, "Overlays")
        if save_as_png:
            os.makedirs(overlay_path, exist_ok=True)

        overlays_jarr = []
        for map_overlay in self.overlay_manager.get_overlays():
            b = map_overlay.get_bitmap()
            if b is None:
                continue

            overlay_jobj = {
                "X": map_overlay.position[0],
                "Y": map_overlay.position[1],
                "Name": map_overlay.overlay_name,
                "Type": map_overlay.type_name,
                "Width": b.width,
                "Height": b.height,
                "DlcGuard": map_overlay.dlc_guard,
                "IsSecret": map_overlay.is_secret,
            }

            if map_overlay.zoom_level_visibility != 0:
                overlay_jobj["ZoomLevelVisibility"] = map_overlay.zoom_level_visibility

            overlays_jarr.append(overlay_jobj)
            if save_as_png and not os.path.exists(os.path.join(overlay_path, f"{map_overlay.overlay_name}.png")):
                b.save(os.path.join(overlay_path, f"{map_overlay.overlay_name}.png"))

        with open(os.path.join(path, "Overlays.json"), "w") as f:
            json.dump(overlays_jarr, f, indent=4)

    def update_edge_coords(self, node):
        if self.min_x > node.x:
            self.min_x = node.x
        if self.max_x < node.x:
            self.max_x = node.x
        if self.min_z > node.z:
            self.min_z = node.z
        if self.max_z < node.z:
            self.max_z = node.z

    def get_node_by_uid(self, uid):
        return self.nodes.get(uid)

    def get_country_by_token_name(self, name):
        token = ScsToken.string_to_token(name)
        return self.countries_lookup.get(token)

    def lookup_road_look(self, look_id):
        return self.road_lookup.get(look_id)

    def lookup_prefab(self, prefab_id):
        return self.prefab_lookup.get(prefab_id)

    def lookup_city(self, city_id):
        return self.cities_lookup.get(city_id)

    def lookup_ferry_connection(self, ferry_port_id):
        return [item for item in self.ferry_connection_lookup if item.start_port_token == ferry_port_id]

    def add_ferry_port_location(self, ferry_port_id, x, z):
        ferry = [item for item in self.ferry_connection_lookup if item.start_port_token == ferry_port_id or item.end_port_token == ferry_port_id]
        for connection in ferry:
            connection.set_port_location(ferry_port_id, x, z)
