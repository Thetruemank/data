import tkinter as tk
from TsMap import TsMapper

class TsMapCanvas(tk.Canvas):
    def __init__(self, master=None, path="", mods=[]):
        super().__init__(master)
        self.master = master
        self.path = path
        self.mods = mods
        self.mapper = TsMapper(path, mods)
        self.mapper.parse()
        self.scale = 0.2
        self.start_point = (-1000, -4000) if self.mapper.is_ets2 else (-105000, 15000)
        self.dragging = False
        self.last_point = (0, 0)
        self.create_widgets()
        self.bind_events()

    def create_widgets(self):
        self.pack(fill=tk.BOTH, expand=True)

    def bind_events(self):
        self.bind("<ButtonPress-1>", self.on_mouse_down)
        self.bind("<ButtonRelease-1>", self.on_mouse_up)
        self.bind("<B1-Motion>", self.on_mouse_move)
        self.bind("<MouseWheel>", self.on_mouse_wheel)
        self.bind("<Configure>", self.on_resize)

    def on_mouse_down(self, event):
        self.dragging = True
        self.last_point = (event.x, event.y)

    def on_mouse_up(self, event):
        self.dragging = False

    def on_mouse_move(self, event):
        if self.dragging:
            self.start_point = (
                self.start_point[0] - (event.x - self.last_point[0]) / self.scale,
                self.start_point[1] - (event.y - self.last_point[1]) / self.scale,
            )
            self.last_point = (event.x, event.y)
            self.redraw_map()

    def on_mouse_wheel(self, event):
        self.scale += (1 if event.delta > 0 else -1) * 0.05 * self.scale
        self.scale = max(self.scale, 0.0005)
        self.redraw_map()

    def on_resize(self, event):
        self.redraw_map()

    def redraw_map(self):
        self.delete("all")
        # Add code to render the map using self.mapper and self.scale

if __name__ == "__main__":
    root = tk.Tk()
    root.title("TsMapCanvas")
    root.geometry("800x600")
    app = TsMapCanvas(master=root)
    app.mainloop()
