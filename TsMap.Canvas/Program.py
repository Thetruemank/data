import tkinter as tk
from TsMapCanvas import TsMapCanvas

class SetupForm(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("Setup Form")
        self.geometry("400x300")
        self.create_widgets()

    def create_widgets(self):
        self.label = tk.Label(self, text="Setup Form")
        self.label.pack(pady=20)

        self.start_button = tk.Button(self, text="Start", command=self.start_application)
        self.start_button.pack(pady=20)

    def start_application(self):
        self.destroy()
        app = TsMapCanvas()
        app.mainloop()

if __name__ == "__main__":
    app = SetupForm()
    app.mainloop()
