from __future__ import annotations

import render_ankara_comstock_v3 as base
import render_ankara_comstock_v4 as authentic


# Preserve the complete ComstockMk01 silhouette: top opening, cylinder walls,
# both ears, and the bottom contour. Nothing in the head asset is cut away.
authentic.PRESERVE_FULL_HEAD = True
base.STEM = "Ankara_Comstock_FullHead"
base.draw_robot_face = authentic.draw_authentic_comstock_head


if __name__ == "__main__":
    base.main()
