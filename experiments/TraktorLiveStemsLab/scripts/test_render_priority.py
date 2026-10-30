import unittest
from gpu_worker import next_window
from stem_cache import BLOCK


class SeekPriorityTests(unittest.TestCase):
    def test_seek_keeps_overlap_neighbors_ahead_of_background_work(self):
        hop=172*BLOCK;requested=172*40;done=set()
        order=[]
        for _ in range(4):
            index=next_window(requested,hop,100,done);order.append(index);done.add(index)
        self.assertEqual(order,[40,39,41,0])

    def test_new_seek_supersedes_previous_position_and_edges_remain_valid(self):
        hop=172*BLOCK
        self.assertEqual(next_window(172*70,hop,100,{40,39}),70)
        self.assertEqual(next_window(0,hop,100,{0}),1)
        self.assertEqual(next_window(172*99,hop,100,{99}),98)


if __name__=='__main__':unittest.main()
