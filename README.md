# BlendTreeBuilder

### A Unity tool to make VRC Blendtree creation easier and faster

Currently, this tool is only capable of handling toggle layers for the most part.  
It is not perfect by any means but will hopefully prove useful in optimizing your toggles.

# Installation
1. Download the Unity package from [releases.](https://github.com/Dreadrith/BlendTreeBuilder/releases)
2. Import the Unity package into Unity.

# How to use
1. Open the window by finding it in the toolbar: DreadTools > BlendTreeBuilder
2. Make sure that the FX Controller set is the controller you want to optimize and press Next.
3. Press 'Optimize!' at the bottom.
4. Done!

![ready window](https://github.com/Dreadrith/BlendTreeBuilder/raw/main/media~/wind1.png)

# Details
On the second step, in the optimize tab, you're given details on what will be handled.
- 'Make Duplicate' will make a backup of your controller before proceeding.
- 'Replace' will delete the layer for the toggle that will be optimized.
- 'Active' will determine with this toggle will be handled or not.
- Yellow warning icon appears when transitions in the toggle layer are not instant, such as dissolve toggles. This may cause a change in the behaviour of the toggle when optimized.
- Red warning icon appears when the parameter for the toggle is not a float and other layers are re-using the parameter. This means that the other layers will likely not work as intended if the parameter is converted to a float for the blendtree.
- Foldout is to see or change what start and end motions will be used for this toggle.

![optimize window](https://github.com/Dreadrith/BlendTreeBuilder/raw/main/media~/wind2.png)

### Warning
The optimizer does not take into account layer priority. If an optimized toggle has overlapping clips with another clip, there may be change in behaviour where properties get overwritten.

## Planned Features
- Handle dedicated motion time layers
- Handle dedicated single blendtree layers
- Implement float smoothing for clip blending
- Make the builder for faster and easier tree building

![tree preview](https://github.com/Dreadrith/BlendTreeBuilder/raw/main/media~/wind3.png)
