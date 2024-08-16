# BlendTreeBuilder
A Unity tool to make VRC Blendtree creation easier and faster 

### [Download From Here](https://vpm.dreadscripts.com/)

## Features
- Simple toggles
- Single State layers, including motion time states.
- Exclusive Toggles

# How to use
1. Open the window by finding it in the toolbar: DreadTools > BlendTreeBuilder
2. Make sure that the FX Controller set is the controller you want to optimize and press Next.
3. Press 'Optimize!' at the bottom.
4. Done!

![ready window](https://i.imgur.com/aGnwx2T.png)

# Details
On the second step, in the optimize tab, you're given details on what will be handled.
- 'Make Duplicate' will make a backup of your controller before proceeding.
- 'Replace' will delete the layer for the toggle that will be optimized.
- 'Active' will determine wether this toggle will be handled or not.
- Yellow warning icon appears if the toggle behaviour may change when optimized, such as with dissolve toggles.
- Red warning icon appears if optimizing this toggle may break some functionality, such as with exclusive toggles through parameter drivers.
- Foldout is to see or change what start and end motions will be used for this toggle.

![optimize window](https://i.imgur.com/QIDZTdq.png)

### Notes
You should almost always make backups in case something doesn't work right.  
After running the tool, you should test whether they work with [this emulator](https://github.com/lyuma/Av3Emulator).  
If something doesn't work, you can go back to optimize the original again and disable 'Active' for the toggles that didn't work.

### Warning
The optimizer does not take into account layer priority. If an optimized toggle has overlapping clips with another clip, there may be change in behaviour where properties get overwritten.

## Planned Features
- Implement float smoothing for clip blending. i.e: dissolves
- Make the builder for faster and easier tree building

![tree preview](https://i.imgur.com/M0L2E8G.png)

### Thank You
If you enjoy BlendTreeBuilder, please consider [supporting me ♡](https://ko-fi.com/Dreadrith)!
