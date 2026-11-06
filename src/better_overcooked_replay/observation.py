"""Small semantic RGB observations: no display, font, Unity or animation dependency."""
import cv2
import numpy as np
from .recipes import scoped_recipes


class PixelObserver:
    def __init__(self, image_size=128):
        if image_size not in (84, 128):
            raise ValueError('image_size must be 84 or 128')
        self.image_size = image_size

    @scoped_recipes
    def render(self, mdp, state, player_index=0, horizon=400):
        size = self.image_size
        image = np.full((size, size, 3), (20, 25, 34), dtype=np.uint8)
        tile = max(1, min(size // mdp.width, (size-5) // mdp.height))
        ox, oy = (size-mdp.width*tile)//2, 5+(size-5-mdp.height*tile)//2
        terrain_colors = {' ': (224,214,187), 'X': (125,91,64), 'P': (67,76,88),
                          'O': (215,173,65), 'T': (196,70,67), 'D': (192,221,226), 'S': (88,173,119)}

        def center(position):
            return (ox + int(position[0])*tile + tile//2, oy + int(position[1])*tile + tile//2)

        def draw_object(obj, pos=None, small=False):
            x, y = pos or center(obj.position)
            radius = max(1, tile//(6 if small else 4))
            color = {'onion': (240,200,85), 'tomato': (230,76,65),
                     'dish': (238,247,250), 'soup': (246,142,45)}[obj.name]
            cv2.circle(image, (x,y), radius, color, -1)
            if obj.name == 'dish':
                cv2.circle(image, (x,y), max(1,radius-1), (125,176,199), 1)

        for y, row in enumerate(mdp.terrain_mtx):
            for x, terrain in enumerate(row):
                a, b = ox+x*tile, oy+y*tile
                cv2.rectangle(image, (a,b), (a+tile-2,b+tile-2), terrain_colors[terrain], -1)
        for obj in state.objects.values():
            x, y = center(obj.position)
            draw_object(obj)
            if obj.name == 'soup':
                for j, name in enumerate(obj.ingredients):
                    cv2.circle(image, (x-tile//3+j*max(2,tile//4),y-tile//3), max(1,tile//10),
                               (247,207,82) if name == 'onion' else (238,75,64), -1)
                ratio = 0 if obj.is_idle else min(1, obj._cooking_tick / max(1,obj.cook_time))
                cv2.rectangle(image, (x-tile//3,y+tile//3),
                              (x-tile//3+int(2*tile//3*ratio),y+tile//3+1),
                              (70,240,125) if obj.is_ready else (250,165,65), -1)
        for i, player in enumerate(state.players):
            x,y = center(player.position)
            color = (61,133,228) if i == player_index else (231,122,64)
            cv2.circle(image, (x,y), max(2,tile//3), color, -1)
            dx,dy = player.orientation
            cv2.line(image, (x,y), (x+dx*tile//3,y+dy*tile//3), (255,255,255), max(1,tile//10))
            if player.held_object:
                draw_object(player.held_object, (x+tile//4,y+tile//4), True)
        remaining = max(0, min(1, (horizon-state.timestep) / max(1,horizon)))
        cv2.rectangle(image, (0,0), (int((size-1)*remaining),2), (110,209,181), -1)
        return image
